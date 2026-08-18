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
        "Como você quer chamar o repositório? Me manda só o nome (ex.: <code>diario</code>).";

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
    /// Placeholder for the states whose behaviour lands in the next slices. It exists so no
    /// path can end in silence: a message accepted without a reply is the bug we're avoiding.
    /// </summary>
    public const string ComingSoon =
        "Recebi sua mensagem! O commit em si ainda está sendo ligado — te aviso assim que estiver de pé.";
}
