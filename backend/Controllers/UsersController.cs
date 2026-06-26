using Microsoft.AspNetCore.Mvc;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController(DatabaseService db) : ControllerBase
{
    [HttpGet]
    public IActionResult GetAll() => Ok(db.GetAllUsers());

    [HttpPost]
    public IActionResult Add([FromBody] AddUserRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.UserName) || string.IsNullOrWhiteSpace(req.DisplayName))
            return BadRequest("UserName と DisplayName は必須です。");

        var role = req.Role is "admin" or "user" ? req.Role : "user";
        try
        {
            var user = db.AddUser(req.UserName.Trim(), req.DisplayName.Trim(), role);
            return Ok(user);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return BadRequest($"UserName '{req.UserName}' は既に登録されています。");
        }
    }

    [HttpDelete("{userName}")]
    public IActionResult Delete(string userName)
    {
        var deleted = db.DeleteUser(userName);
        return deleted ? NoContent() : NotFound();
    }
}
