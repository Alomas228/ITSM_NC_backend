using Microsoft.AspNetCore.Identity;

namespace ItsmNc.Api.Models;

public class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.NewGuid();
    }

    public bool IsActive { get; set; } = true;

    // У администратора тенантов нет привязки к отдельному тенанту.
    public Guid? TenantId { get; set; }
}
