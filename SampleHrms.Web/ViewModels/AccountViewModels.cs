using System.ComponentModel.DataAnnotations;

namespace SampleHrms.Web.ViewModels;

public class LoginViewModel
{
    [Required, EmailAddress]
    [System.ComponentModel.DataAnnotations.Display(Name = "Email")]
    public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
}

public class CreateEmployeeViewModel
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required] public string FullName { get; set; } = "";
    [Required] public string EmployeeCode { get; set; } = "";
    [Required] public string Department { get; set; } = "";
    [Required, MinLength(6)] public string Password { get; set; } = "";
    [Required] public string Role { get; set; } = "Employee";
}
