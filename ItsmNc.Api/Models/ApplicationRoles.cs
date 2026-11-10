namespace ItsmNc.Api.Models;

public static class ApplicationRoles
{
    // РОЛ-01: администратор тенантов, используется только в SaaS.
    public const string TenantAdministrator = "TenantAdministrator";

    // РОЛ-02: администратор отдельного тенанта.
    public const string Administrator = "Administrator";

    // РОЛ-03: инженер технической поддержки.
    public const string SupportEngineer = "SupportEngineer";

    // РОЛ-04: руководитель, назначаемый из инженеров технической поддержки.
    public const string Supervisor = "Supervisor";

    // РОЛ-05: пользователь портала, сотрудник или внешнее лицо.
    public const string PortalUser = "PortalUser";
}
