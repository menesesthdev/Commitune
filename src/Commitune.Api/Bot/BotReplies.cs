namespace Commitune.Api.Bot;

/// <summary>
/// Every user-facing string, in one place. The bot speaks Portuguese — the commands
/// (<c>/pausar</c>, <c>/desconectar</c>) already do.
///
/// Everything here is sent with Telegram's HTML parse mode, so any value interpolated into a
/// reply has to go through <see cref="Escape"/> first. A single <c>&lt;</c> in a user's title
/// is enough for the Bot API to reject the whole message — which would turn a successful
/// commit into silence.
/// </summary>
public static class BotReplies
{
    public const string ConnectButtonLabel = "Conectar com o GitHub";

    public const string Welcome =
        "Oi! Eu transformo o que você aprende em <b>TILs</b> (Today I Learned) commitados em " +
        "um repositório <b>privado</b> seu no GitHub.\n\n" +
        "Primeiro passo: autorize o acesso à sua conta no botão abaixo.";

    public const string ConnectAgain =
        "Ainda estou esperando a autorização no GitHub. É só tocar no botão abaixo.";

    public const string FinishAuthFirst =
        "Guardei sua mensagem por enquanto — mas ainda preciso da autorização no GitHub antes " +
        "de conseguir commitar qualquer coisa. Toque no botão abaixo para terminar.";

    public const string AskRepoName =
        "GitHub conectado! Como você quer chamar o repositório? " +
        "Me manda só o nome (ex.: <code>til</code>).";

    /// <summary>The same question asked again later, without the "just connected" framing.</summary>
    public const string AskRepoNameAgain =
        "Ainda falta escolher o nome do repositório. Como quer chamar? " +
        "(ex.: <code>til</code>)";

    public const string Reconnected =
        "Reconectado com o GitHub. Seu repositório continua o mesmo — pode seguir escrevendo.";

    public const string AuthorizationFailed =
        "O GitHub recusou a autorização. Mande /start para tentar de novo.";

    public const string AuthorizationExpired =
        "Sua autorização do GitHub não vale mais — pode ter sido revogada por lá. " +
        "Mande /start para reconectar.";

    /// <summary>Same problem, mid-entry — so it also has to say the message was not committed.</summary>
    public const string AuthorizationExpiredWhileCommitting =
        "Sua autorização do GitHub não vale mais, então essa mensagem <b>não</b> foi commitada. " +
        "Reconecte no botão abaixo e me mande ela de novo.";

    public const string RepoNameTaken =
        "Você já tem um repositório com esse nome. Me manda outro?";

    public const string CreatingRepo =
        "Criando seu repositório privado…";

    /// <summary>The name broke GitHub's rules and we have nothing sensible to suggest.</summary>
    public const string RepoNameInvalid =
        "Esse nome não serve para o GitHub. Vale usar letras, números, <code>-</code>, " +
        "<code>_</code> e <code>.</code> — sem espaços nem acentos. Como quer chamar?";

    /// <summary>Same, but the user's text cleans up into something legal worth offering.</summary>
    public static string RepoNameInvalidWithSuggestion(string suggestion)
        => "Esse nome não serve para o GitHub (nada de espaços ou acentos). " +
            $"Que tal <code>{Escape(suggestion)}</code>? Se topar, é só me mandar de volta.";

    /// <summary>
    /// The convention, taught by example instead of by specification. It is the only thing the
    /// user has to learn, so it is shown at the moment it starts mattering — and nothing in it
    /// is mandatory: a one-line message with no tags is a perfectly good entry.
    /// </summary>
    public const string HowToWrite =
        "Agora é só me contar o que você aprendeu. A <b>primeira linha vira o título</b>, o " +
        "resto vira o conteúdo, e qualquer <code>#tag</code> vira tag do arquivo.\n\n" +
        "<pre>Índice não entra quando a coluna tem função\n" +
        "No Postgres, WHERE lower(email) = ... ignora o índice de email. " +
        "Precisa de índice na expressão. #postgres #indices</pre>";

    public static string RepoCreated(string owner, string name)
        => $"Pronto! Criei <b>{Escape(owner)}/{Escape(name)}</b>, privado, e já está tudo ligado.\n\n"
            + HowToWrite;

    public const string AlreadyReady =
        "Tudo certo por aqui — é só me mandar o que você aprendeu que eu commito. " +
        "Use /pausar se quiser dar uma pausa.";

    public const string Resumed =
        "Voltamos! Manda o próximo TIL que eu commito.";

    public const string Paused =
        "Pausado. Não vou commitar nada até você mandar /start de novo.";

    public const string PausedReminder =
        "Estou pausado no momento — mande /start para voltar a commitar.";

    public const string NothingToPause =
        "Você ainda não terminou a conexão com o GitHub, então não há nada para pausar. " +
        "Mande /start para começar.";

    public const string StartFirst =
        "Ainda não nos conhecemos! Mande /start para conectar sua conta do GitHub.";

    public const string UnknownCommand =
        "Não conheço esse comando. Os que eu entendo são: /start e /pausar.";

    public const string NotAvailableYet =
        "Esse comando ainda não está disponível — estou terminando essa parte.";

    public const string UnsupportedMessage =
        "Por enquanto eu só entendo mensagens de texto.";

    public const string SomethingWentWrong =
        "Algo deu errado do meu lado e sua mensagem não foi commitada. " +
        "Tente de novo em instantes — se continuar, mande /start para eu revisar sua conexão.";

    /// <summary>
    /// The one reply the product exists for. It echoes the title and the tags because the user
    /// wrote free-form text and the bot inferred both — showing them is how the convention gets
    /// learned, and how a title that came out wrong gets noticed while it is still fresh.
    /// </summary>
    public static string EntryCommitted(string? title, IReadOnlyList<string>? tags, Uri? url)
    {
        var reply = "TIL registrado ✅";

        if (title is { Length: > 0 })
        {
            reply += $"\n\n<b>{Escape(title)}</b>";
        }

        if (tags is { Count: > 0 })
        {
            reply += $"\n🏷 {Escape(string.Join(", ", tags))}";
        }

        if (url is not null)
        {
            reply += $"\n\n<a href=\"{url}\">ver no GitHub</a>";
        }

        return reply;
    }

    /// <summary>
    /// The repository is not there anymore. Says outright that the message was lost — the user
    /// will find out either way, and finding out from the commit that never appeared is worse.
    /// </summary>
    public const string RepositoryMissing =
        "Não encontrei mais o repositório onde eu commitava — ele pode ter sido apagado ou " +
        "renomeado no GitHub, e por isso essa mensagem não foi salva.\n\n" +
        "Como você quer chamar o novo repositório?";

    public const string CommitFailed =
        "O GitHub não aceitou o commit agora, então sua mensagem <b>não</b> foi salva. " +
        "Me manda de novo daqui a pouco?";

    /// <summary>
    /// Telegram's HTML parse mode only needs these three escaped. Deliberately not
    /// <c>WebUtility.HtmlEncode</c>, which also turns every accented letter into a numeric
    /// entity — every Portuguese reply would arrive full of <c>&amp;#205;</c>.
    /// </summary>
    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
