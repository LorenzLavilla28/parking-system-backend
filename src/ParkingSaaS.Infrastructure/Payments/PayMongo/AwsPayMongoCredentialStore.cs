using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;

namespace ParkingSaaS.Infrastructure.Payments.PayMongo;

public sealed class AwsPayMongoCredentialStore : IPayMongoCredentialStore
{
    private readonly IAmazonSecretsManager _secrets;
    private readonly AwsSecretsOptions _options;
    private readonly ILogger<AwsPayMongoCredentialStore> _logger;

    public AwsPayMongoCredentialStore(
        IAmazonSecretsManager secrets,
        IOptions<AwsSecretsOptions> options,
        ILogger<AwsPayMongoCredentialStore> logger)
    {
        _secrets = secrets;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> CreateOrUpdateAsync(
        string secretName,
        PayMongoSecretValues values,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("AWS Secrets Manager integration is disabled.");

        var payload = JsonSerializer.Serialize(values);
        try
        {
            var created = await _secrets.CreateSecretAsync(new CreateSecretRequest
            {
                Name = secretName,
                Description = "Tenant-owned PayMongo credentials for ParkingSaaS.",
                SecretString = payload,
                Tags =
                [
                    new Tag { Key = "Application", Value = "ParkingSaaS" },
                    new Tag { Key = "Provider", Value = "PayMongo" }
                ]
            }, cancellationToken);

            return created.ARN;
        }
        catch (ResourceExistsException)
        {
            var updated = await _secrets.PutSecretValueAsync(new PutSecretValueRequest
            {
                SecretId = secretName,
                SecretString = payload
            }, cancellationToken);

            return updated.ARN;
        }
    }

    public async Task<PayMongoSecretValues?> GetAsync(
        string secretArn,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return null;

        var value = await _secrets.GetSecretValueAsync(new GetSecretValueRequest
        {
            SecretId = secretArn
        }, cancellationToken);

        if (string.IsNullOrWhiteSpace(value.SecretString))
            return null;

        try
        {
            return JsonSerializer.Deserialize<PayMongoSecretValues>(value.SecretString);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "PayMongo secret {SecretArn} is not valid JSON.", secretArn);
            return null;
        }
    }
}
