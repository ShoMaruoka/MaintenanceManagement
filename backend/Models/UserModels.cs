namespace MaintenanceManagement.Api.Models;

public class AppUser
{
    public long UserId { get; set; }
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "user";  // "admin" | "user"
}

public class AddUserRequest
{
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "user";
}
