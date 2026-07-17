using System.Security.Cryptography;
using System.Text;

namespace Duets.Pad;

/// <summary>
/// Factory for built-in <see cref="DuetsPadServiceOptions.Authenticate"/> handlers.
/// </summary>
public static class DuetsPadAuthenticator
{
    /// <summary>
    /// Creates an <see cref="DuetsPadServiceOptions.Authenticate"/> handler that accepts requests
    /// whose bearer credential equals <paramref name="token"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This factory exists to keep the constant-time comparison in one place — not to save
    /// keystrokes. The obvious hand-written comparison lambda would compare the credential and the
    /// token with <c>==</c>, which short-circuits on the first mismatched byte and leaks timing
    /// information about the secret; the handler returned here instead compares the UTF-8 bytes of
    /// both strings with <see cref="CryptographicOperations.FixedTimeEquals"/> (ADR-49).
    /// </para>
    /// <para>
    /// The comparison is constant-time with respect to token <i>content</i>, not to its length:
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> returns immediately when the byte
    /// counts differ, so a candidate's length remains observable. That is accepted — the token
    /// length is not itself a secret for the long random tokens this is meant for, and the content
    /// bytes, which are, stay unleaked.
    /// </para>
    /// </remarks>
    /// <param name="token">
    /// The fixed credential to accept. Must be non-null, non-whitespace, and free of leading or
    /// trailing whitespace: the credential is read from an <c>Authorization: Bearer</c> header,
    /// whose surrounding whitespace is not part of the value, so a token carrying any could never
    /// be presented.
    /// </param>
    /// <returns>
    /// A handler that returns <see langword="false"/> when the context carries no credential, and
    /// otherwise compares the presented credential against <paramref name="token"/> in constant time.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="token"/> is <see langword="null"/>, empty, whitespace-only, or has leading or
    /// trailing whitespace.
    /// </exception>
    public static Func<DuetsPadAuthenticationContext, ValueTask<bool>> Token(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException(
                "Token must not be null, empty, or whitespace.",
                nameof(token)
            );
        }

        if (token != token.Trim())
        {
            throw new ArgumentException(
                "Token must not have leading or trailing whitespace; such a token could never be "
                    + "presented in an Authorization header.",
                nameof(token)
            );
        }

        var tokenBytes = Encoding.UTF8.GetBytes(token);

        return context =>
        {
            if (context.Credential is not { } credential)
            {
                return new ValueTask<bool>(false);
            }

            var credentialBytes = Encoding.UTF8.GetBytes(credential);
            return new ValueTask<bool>(
                CryptographicOperations.FixedTimeEquals(credentialBytes, tokenBytes)
            );
        };
    }
}
