using Commitune.Domain.Entities;

namespace Commitune.Api.Bot;

/// <summary>
/// Undoes the connection: revokes the authorization on GitHub and wipes everything Commitune
/// kept about it. Separate from the conversation for the same reason as the other two — the
/// order of operations here is a security property, not a detail of the reply.
/// </summary>
public interface IAccountDisconnector
{
    Task<DisconnectOutcome> DisconnectAsync(BotUser user, CancellationToken cancellationToken);
}

public enum DisconnectOutcome
{
    /// <summary>Revoked on GitHub and wiped here. The user is back to <c>NotStarted</c>.</summary>
    Disconnected,

    /// <summary>
    /// Wiped here, but GitHub did not confirm the revocation. The user has to be told, because
    /// the grant may still be listed on their GitHub account.
    /// </summary>
    DisconnectedWithoutRevoking,

    /// <summary>There was nothing connected to begin with.</summary>
    NothingToDisconnect,
}
