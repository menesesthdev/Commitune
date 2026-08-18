namespace Commitune.Api.Bot;

/// <summary>
/// Every user-facing string, in one place. The bot speaks Portuguese — the commands
/// (<c>/pausar</c>, <c>/desconectar</c>) already do.
/// </summary>
public static class BotReplies
{
    public const string ConnectButtonLabel = "Conectar com o GitHub";

    public const string Welcome =
        "Oi! Eu transformo suas mensagens em commits em um repositório <b>privado</b> seu no GitHub.\n\n" +
        "Primeiro passo: autorize o acesso à sua conta no botão abaixo.";

    public const string ConnectAgain =
        "Ainda estou esperando a autorização no GitHub. É só tocar no botão abaixo.";

    public const string FinishAuthFirst =
        "Guardei sua mensagem por enquanto — mas ainda preciso da autorização no GitHub antes " +
        "de conseguir commitar qualquer coisa. Toque no botão abaixo para terminar.";

    public const string AskRepoName =
        "GitHub conectado! Como você quer chamar o repositório? " +
        "Me manda só o nome (ex.: <code>diario</code>).";

    /// <summary>The same question asked again later, without the "just connected" framing.</summary>
    public const string AskRepoNameAgain =
        "Ainda falta escolher o nome do repositório. Como quer chamar? " +
        "(ex.: <code>diario</code>)";

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
            $"Que tal <code>{suggestion}</code>? Se topar, é só me mandar de volta.";

    public static string RepoCreated(string owner, string name)
        => $"Pronto! Criei <b>{owner}/{name}</b>, privado, e já está tudo ligado.\n\n" +
            "A partir de agora, toda mensagem que você me mandar vira um commit lá.";

    public const string AlreadyReady =
        "Tudo certo por aqui — é só me mandar uma mensagem que ela vira commit. " +
        "Use /pausar se quiser dar uma pausa.";

    public const string Resumed =
        "Voltamos! Pode mandar sua próxima mensagem que eu commito.";

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
    /// The one reply the product exists for. The link is what turns "it says it worked" into
    /// "I can see it", so it is included whenever GitHub hands one back.
    /// </summary>
    public static string EntryCommitted(Uri? url)
        => url is null
            ? "Commitado ✅"
            : $"Commitado ✅ — <a href=\"{url}\">ver no GitHub</a>";

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
}
