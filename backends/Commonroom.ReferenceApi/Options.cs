namespace Commonroom.ReferenceApi;

public sealed class CognitoOptions
{
    public string Region { get; set; } = string.Empty;
    public string UserPoolId { get; set; } = string.Empty;

    /// <summary>The single app client this project backend accepts tokens from.</summary>
    public string ClientId { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Region) && !string.IsNullOrWhiteSpace(UserPoolId) && !string.IsNullOrWhiteSpace(ClientId);

    public string? Authority =>
        string.IsNullOrWhiteSpace(Region) || string.IsNullOrWhiteSpace(UserPoolId)
            ? null
            : $"https://cognito-idp.{Region}.amazonaws.com/{UserPoolId}";
}

public sealed class ApiKeyOptions
{
    public string[] Allowed { get; set; } = [];
}

public sealed class VectorStoreOptions
{
    /// <summary>vector_store_id values a client may bind a new conversation to.</summary>
    public string[] Allowed { get; set; } = [];
}
