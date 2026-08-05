using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using ParkingSaaS.Contracts.Common;

namespace ParkingSaaS.UnitTests.Common;

public sealed class ApiResponseSerializationTests
{
    [Fact]
    public void Null_data_is_preserved_when_null_properties_are_globally_ignored()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var json = JsonSerializer.Serialize(ApiResponse<string?>.Ok(null), options);

        json.Should().Be("{\"data\":null}");
    }
}
