using System.ComponentModel.DataAnnotations;

namespace ItsmNc.Api.Authentication;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    [Required(ErrorMessage = "Не задан адрес Keycloak:Authority.")]
    public string Authority { get; set; } = string.Empty;

    [Required(ErrorMessage = "Не задан получатель токена Keycloak:Audience.")]
    public string Audience { get; set; } = string.Empty;

    [Required(ErrorMessage = "Не задан клиент ролей Keycloak:ClientId.")]
    public string ClientId { get; set; } = string.Empty;
}
