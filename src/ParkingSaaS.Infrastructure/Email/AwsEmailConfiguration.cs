using System.Text.Json;
using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;
using ParkingSaaS.Application.Common.Options;

namespace ParkingSaaS.Infrastructure.Email;

/// <summary>
/// Adds Microsoft Graph email credentials from AWS Secrets Manager before options
/// are bound. Local development can continue to supply the same values directly.
/// </summary>
public static class AwsEmailConfiguration
{
    private static readonly string[] RequiredSecretFields =
        ["TenantId", "ClientId", "ClientSecret", "FromAddress"];

    public static async Task AddSecretsManagerValuesAsync(
        ConfigurationManager configuration,
        CancellationToken cancellationToken = default)
    {
        var section = configuration.GetSection(EmailOptions.SectionName);
        if (!section.GetValue<bool>(nameof(EmailOptions.Enabled)) || HasDirectCredentials(section))
            return;

        var secretName = section[nameof(EmailOptions.SecretName)];
        if (string.IsNullOrWhiteSpace(secretName))
            return;

        var regionName = configuration[$"{AwsSecretsOptions.SectionName}:{nameof(AwsSecretsOptions.Region)}"]
            ?? "ap-southeast-1";
        using var client = new AmazonSecretsManagerClient(RegionEndpoint.GetBySystemName(regionName));
        var response = await client.GetSecretValueAsync(
            new GetSecretValueRequest { SecretId = secretName },
            cancellationToken);

        if (string.IsNullOrWhiteSpace(response.SecretString))
            throw new InvalidOperationException($"Email secret '{secretName}' is empty.");

        using var document = JsonDocument.Parse(response.SecretString);
        var values = new Dictionary<string, string?>();
        foreach (var field in RequiredSecretFields)
        {
            if (!document.RootElement.TryGetProperty(field, out var property)
                || string.IsNullOrWhiteSpace(property.GetString()))
            {
                throw new InvalidOperationException($"Email secret '{secretName}' is missing '{field}'.");
            }

            values[$"{EmailOptions.SectionName}:{field}"] = property.GetString();
        }

        if (document.RootElement.TryGetProperty(nameof(EmailOptions.FromName), out var fromName)
            && !string.IsNullOrWhiteSpace(fromName.GetString()))
        {
            values[$"{EmailOptions.SectionName}:{nameof(EmailOptions.FromName)}"] = fromName.GetString();
        }

        configuration.AddInMemoryCollection(values);
    }

    private static bool HasDirectCredentials(IConfigurationSection section) =>
        !string.IsNullOrWhiteSpace(section[nameof(EmailOptions.TenantId)])
        && !string.IsNullOrWhiteSpace(section[nameof(EmailOptions.ClientId)])
        && !string.IsNullOrWhiteSpace(section[nameof(EmailOptions.ClientSecret)])
        && !string.IsNullOrWhiteSpace(section[nameof(EmailOptions.FromAddress)]);
}
