using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Domain.Emails;

namespace ParkingSaaS.Infrastructure.Email;

/// <summary>
/// Microsoft Graph app-only email transport. The background email dispatcher uses
/// client credentials, so no interactive Outlook login is required at send time.
/// </summary>
public sealed partial class GraphEmailSender : IEmailSender
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex DataImagePattern = CreateDataImagePattern();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EmailOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public GraphEmailSender(IHttpClientFactory httpClientFactory, IOptions<EmailOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var (html, attachments) = InlineImages(message.HtmlBody);

        var payload = new GraphSendMailRequest(
            new GraphMessage(
                message.Subject,
                new GraphBody("HTML", html),
                [new GraphRecipient(new GraphEmailAddress(message.ToEmail, message.ToName))],
                attachments),
            SaveToSentItems: true);

        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(_options.FromAddress)}/sendMail");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Microsoft Graph email delivery failed ({(int)response.StatusCode}): {detail}");
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return _accessToken;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
                return _accessToken;

            var client = _httpClientFactory.CreateClient();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["scope"] = "https://graph.microsoft.com/.default",
                ["grant_type"] = "client_credentials",
            });

            using var response = await client.PostAsync(
                $"https://login.microsoftonline.com/{Uri.EscapeDataString(_options.TenantId)}/oauth2/v2.0/token",
                content,
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Microsoft identity token request failed ({(int)response.StatusCode}): {body}");

            var token = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions)
                ?? throw new InvalidOperationException("Microsoft identity returned an empty token response.");
            _accessToken = token.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static (string Html, IReadOnlyList<GraphAttachment> Attachments) InlineImages(string html)
    {
        var attachments = new List<GraphAttachment>();
        var transformed = DataImagePattern.Replace(html, match =>
        {
            try
            {
                var contentType = match.Groups[1].Value;
                var contentId = $"inline-{Guid.NewGuid():N}";
                var bytes = Convert.FromBase64String(match.Groups[2].Value);
                attachments.Add(new GraphAttachment(
                    "#microsoft.graph.fileAttachment",
                    $"{contentId}.bin",
                    contentType,
                    Convert.ToBase64String(bytes),
                    true,
                    contentId));
                return $"src=\"cid:{contentId}\"";
            }
            catch (FormatException)
            {
                return match.Value;
            }
        });

        return (transformed, attachments);
    }

    [GeneratedRegex("src=\\\"data:(image/[^;\\\"]+);base64,([^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreateDataImagePattern();

    private sealed record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record GraphSendMailRequest(GraphMessage Message, bool SaveToSentItems);
    private sealed record GraphMessage(
        string Subject,
        GraphBody Body,
        IReadOnlyList<GraphRecipient> ToRecipients,
        IReadOnlyList<GraphAttachment> Attachments);
    private sealed record GraphBody(string ContentType, string Content);
    private sealed record GraphRecipient(GraphEmailAddress EmailAddress);
    private sealed record GraphEmailAddress(string Address, string? Name);
    private sealed record GraphAttachment(
        [property: System.Text.Json.Serialization.JsonPropertyName("@odata.type")]
        string OdataType,
        string Name,
        string ContentType,
        string ContentBytes,
        bool IsInline,
        string ContentId);
}
