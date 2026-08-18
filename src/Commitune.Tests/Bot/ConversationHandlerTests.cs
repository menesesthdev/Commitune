using Commitune.Api.Bot;
using Commitune.Domain.Entities;
using Commitune.Domain.Onboarding;
using Commitune.Infrastructure.Configuration;
using Commitune.Infrastructure.Security;
using Commitune.Tests.Bot.Fakes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Commitune.Tests.Bot;

public class ConversationHandlerTests
{
    private const long TelegramUserId = 4242;
    private const long ChatId = 909090;

    private readonly FakeBotUserStore _users = new();
    private readonly RecordingBotMessenger _messenger = new();

    private readonly DataProtectionOAuthStateProtector _stateProtector = new(
        DataProtectionProvider.Create("commitune-tests"),
        Options.Create(new GitHubOptions()));

    private readonly FakeRepositoryProvisioner _provisioner = new();
    private readonly FakeEntryCommitter _committer = new();

    private ConversationHandler CreateHandler()
        => new(_users, _messenger, _stateProtector, new StubGitHubOAuthService(), _provisioner, _committer);

    private Task HandleAsync(OnboardingState state, string text)
    {
        _user = _users.Seed(TelegramUserId, state, ChatId);

        return CreateHandler().HandleAsync(_user, text, CancellationToken.None);
    }

    private BotUser? _user;

    private OnboardingState StateOf() => _user!.State;

    [Fact]
    public async Task Start_from_scratch_moves_the_user_to_awaiting_auth()
    {
        await HandleAsync(OnboardingState.NotStarted, "/start");

        Assert.Equal(OnboardingState.AwaitingGithubAuth, StateOf());
        Assert.Equal(1, _users.SaveCount);
    }

    [Fact]
    public async Task Start_from_scratch_sends_the_authorization_link()
    {
        await HandleAsync(OnboardingState.NotStarted, "/start");

        var sent = _messenger.Single;
        Assert.Equal(ChatId, sent.ChatId);
        Assert.NotNull(sent.Link);
        Assert.StartsWith("https://github.com/login/oauth/authorize", sent.Link.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_authorization_link_carries_a_signed_state_not_the_raw_telegram_id()
    {
        await HandleAsync(OnboardingState.NotStarted, "/start");

        var link = _messenger.Single.Link!.ToString();

        // The raw id in the URL would be the account-takeover vector CLAUDE.md calls out.
        Assert.DoesNotContain(TelegramUserId.ToString(), link, StringComparison.Ordinal);

        var state = Uri.UnescapeDataString(link.Split("state=")[1]);
        Assert.True(_stateProtector.TryValidate(state, out var recovered));
        Assert.Equal(TelegramUserId, recovered);
    }

    [Fact]
    public async Task Start_again_while_awaiting_auth_reissues_the_link_without_changing_state()
    {
        await HandleAsync(OnboardingState.AwaitingGithubAuth, "/start");

        Assert.Equal(OnboardingState.AwaitingGithubAuth, StateOf());
        Assert.Equal(0, _users.SaveCount);
        Assert.NotNull(_messenger.Single.Link);
    }

    [Fact]
    public async Task Start_resumes_a_paused_user()
    {
        await HandleAsync(OnboardingState.Paused, "/start");

        Assert.Equal(OnboardingState.Ready, StateOf());
        Assert.Equal(BotReplies.Resumed, _messenger.Single.Text);
    }

    [Fact]
    public async Task Start_while_ready_reports_status_and_leaves_the_user_ready()
    {
        await HandleAsync(OnboardingState.Ready, "/start");

        Assert.Equal(OnboardingState.Ready, StateOf());
        Assert.Equal(BotReplies.AlreadyReady, _messenger.Single.Text);
    }

    [Fact]
    public async Task Pause_stops_a_ready_user()
    {
        await HandleAsync(OnboardingState.Ready, "/pausar");

        Assert.Equal(OnboardingState.Paused, StateOf());
        Assert.Equal(BotReplies.Paused, _messenger.Single.Text);
    }

    [Theory]
    [InlineData(OnboardingState.NotStarted)]
    [InlineData(OnboardingState.AwaitingGithubAuth)]
    [InlineData(OnboardingState.AwaitingRepoName)]
    public async Task Pause_before_onboarding_finishes_changes_nothing(OnboardingState state)
    {
        await HandleAsync(state, "/pausar");

        Assert.Equal(state, StateOf());
        Assert.Equal(BotReplies.NothingToPause, _messenger.Single.Text);
    }

    /// <summary>
    /// The rule from CLAUDE.md: mid-onboarding text is part of the conversation, never an
    /// entry to commit. While waiting on GitHub there is nothing it could mean.
    /// </summary>
    [Fact]
    public async Task Text_received_while_awaiting_auth_is_not_treated_as_an_entry()
    {
        await HandleAsync(OnboardingState.AwaitingGithubAuth, "hoje eu implementei o webhook");

        Assert.Equal(OnboardingState.AwaitingGithubAuth, StateOf());
        Assert.Equal(0, _users.SaveCount);
        Assert.Single(_messenger.Sent);
    }

    /// <summary>
    /// Same rule, other side: while waiting on the name, the text *is* the name — it goes to
    /// the provisioner, not to a commit.
    /// </summary>
    [Fact]
    public async Task Text_received_while_awaiting_the_repo_name_is_the_repo_name()
    {
        await HandleAsync(OnboardingState.AwaitingRepoName, "  diario  ");

        Assert.Equal("  diario  ", _provisioner.RequestedName);
        Assert.Equal(OnboardingState.Ready, StateOf());
    }

    [Fact]
    public async Task A_created_repository_is_confirmed_by_name_to_the_user()
    {
        await HandleAsync(OnboardingState.AwaitingRepoName, "diario");

        var confirmation = _messenger.Sent[^1].Text;
        Assert.Contains("tester/diario", confirmation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_name_already_in_use_asks_for_another_one_without_advancing()
    {
        _provisioner.Result = new RepositoryProvisionResult(RepositoryProvisionOutcome.NameAlreadyTaken);

        await HandleAsync(OnboardingState.AwaitingRepoName, "diario");

        Assert.Equal(OnboardingState.AwaitingRepoName, StateOf());
        Assert.Equal(BotReplies.RepoNameTaken, _messenger.Sent[^1].Text);
    }

    [Fact]
    public async Task An_invalid_name_offers_the_cleaned_up_version()
    {
        _provisioner.Result = new RepositoryProvisionResult(
            RepositoryProvisionOutcome.InvalidName, Suggestion: "meu-diario");

        await HandleAsync(OnboardingState.AwaitingRepoName, "meu diário");

        Assert.Equal(OnboardingState.AwaitingRepoName, StateOf());
        Assert.Contains("meu-diario", _messenger.Sent[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_invalid_name_with_nothing_to_suggest_still_explains_the_rules()
    {
        _provisioner.Result = new RepositoryProvisionResult(RepositoryProvisionOutcome.InvalidName);

        await HandleAsync(OnboardingState.AwaitingRepoName, "???");

        Assert.Equal(BotReplies.RepoNameInvalid, _messenger.Sent[^1].Text);
    }

    /// <summary>
    /// An expired authorization is the failure most likely to strand a user, so the reply is
    /// the reconnect link itself — not a message telling them to go find it.
    /// </summary>
    [Fact]
    public async Task An_expired_authorization_replies_with_a_fresh_link()
    {
        _provisioner.Result = new RepositoryProvisionResult(RepositoryProvisionOutcome.AuthorizationExpired);

        await HandleAsync(OnboardingState.AwaitingRepoName, "diario");

        Assert.Equal(OnboardingState.AwaitingGithubAuth, StateOf());
        Assert.NotNull(_messenger.Sent[^1].Link);
    }

    [Fact]
    public async Task Text_received_while_awaiting_auth_reoffers_the_link()
    {
        await HandleAsync(OnboardingState.AwaitingGithubAuth, "minha primeira anotação");

        Assert.NotNull(_messenger.Single.Link);
    }

    /// <summary>
    /// The whole point of the product: a Ready user's message is an entry, and it goes to the
    /// committer exactly as typed — trimming and formatting belong further down.
    /// </summary>
    [Fact]
    public async Task Text_received_while_ready_is_committed_as_an_entry()
    {
        await HandleAsync(OnboardingState.Ready, "hoje eu terminei a fatia 3");

        Assert.Equal("hoje eu terminei a fatia 3", _committer.CommittedText);
        Assert.Equal(OnboardingState.Ready, StateOf());
    }

    [Fact]
    public async Task A_committed_entry_is_confirmed_with_the_link_to_it()
    {
        await HandleAsync(OnboardingState.Ready, "uma anotação");

        Assert.Contains(FakeEntryCommitter.EntryUrl.ToString(), _messenger.Single.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_committed_entry_is_confirmed_even_when_github_returns_no_link()
    {
        _committer.Result = new EntryCommitResult(EntryCommitOutcome.Committed);

        await HandleAsync(OnboardingState.Ready, "uma anotação");

        Assert.Equal(BotReplies.EntryCommitted(null), _messenger.Single.Text);
    }

    /// <summary>
    /// A failed commit that says nothing is the churn risk CLAUDE.md calls out by name: the
    /// user believes the entry is safe, and finds out days later that it never existed.
    /// </summary>
    [Fact]
    public async Task A_failed_commit_says_the_message_was_not_saved()
    {
        _committer.Result = new EntryCommitResult(EntryCommitOutcome.Failed);

        await HandleAsync(OnboardingState.Ready, "uma anotação");

        Assert.Equal(BotReplies.CommitFailed, _messenger.Single.Text);
        Assert.Equal(OnboardingState.Ready, StateOf());
    }

    [Fact]
    public async Task An_expired_authorization_mid_entry_replies_with_a_fresh_link()
    {
        _committer.Result = new EntryCommitResult(EntryCommitOutcome.AuthorizationExpired);

        await HandleAsync(OnboardingState.Ready, "uma anotação");

        Assert.Equal(OnboardingState.AwaitingGithubAuth, StateOf());
        Assert.NotNull(_messenger.Single.Link);
    }

    /// <summary>
    /// The repository is gone, so the user is back on the naming step — and the reply has to
    /// be the question they are now expected to answer.
    /// </summary>
    [Fact]
    public async Task A_missing_repository_asks_for_a_new_name()
    {
        _committer.Result = new EntryCommitResult(EntryCommitOutcome.RepositoryMissing);

        await HandleAsync(OnboardingState.Ready, "uma anotação");

        Assert.Equal(OnboardingState.AwaitingRepoName, StateOf());
        Assert.Equal(BotReplies.RepositoryMissing, _messenger.Single.Text);
    }

    [Fact]
    public async Task Text_received_while_paused_says_so_instead_of_going_silent()
    {
        await HandleAsync(OnboardingState.Paused, "uma anotação qualquer");

        Assert.Equal(OnboardingState.Paused, StateOf());
        Assert.Equal(BotReplies.PausedReminder, _messenger.Single.Text);
    }

    [Theory]
    [InlineData("/start@commitune_bot")]
    [InlineData("/START")]
    [InlineData("  /start")]
    [InlineData("/start extra")]
    public async Task Start_is_recognized_in_the_shapes_telegram_delivers(string text)
    {
        await HandleAsync(OnboardingState.NotStarted, text);

        Assert.Equal(OnboardingState.AwaitingGithubAuth, StateOf());
    }

    [Fact]
    public async Task An_unknown_command_is_not_committed_as_an_entry()
    {
        await HandleAsync(OnboardingState.Ready, "/inventado");

        Assert.Equal(BotReplies.UnknownCommand, _messenger.Single.Text);
    }

    public static TheoryData<OnboardingState, string> EveryStateAndInput()
    {
        var data = new TheoryData<OnboardingState, string>();

        foreach (var state in Enum.GetValues<OnboardingState>())
        {
            foreach (var text in new[] { "/start", "/pausar", "/repo", "/desconectar", "/inventado", "uma anotação" })
            {
                data.Add(state, text);
            }
        }

        return data;
    }

    /// <summary>
    /// The product-level guarantee: no combination of state and input may leave the user
    /// without an answer. Silence is the churn risk, so it is asserted exhaustively.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryStateAndInput))]
    public async Task Every_state_and_input_produces_a_reply(OnboardingState state, string text)
    {
        await HandleAsync(state, text);

        Assert.NotEmpty(_messenger.Sent);
        Assert.All(_messenger.Sent, sent =>
        {
            Assert.Equal(ChatId, sent.ChatId);
            Assert.False(string.IsNullOrWhiteSpace(sent.Text));
        });
    }
}
