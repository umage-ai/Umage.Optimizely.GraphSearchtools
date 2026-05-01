using System.ComponentModel.DataAnnotations;

namespace GraphSearchtools.SampleSiteCms13.Models;

public class LoginViewModel
{
    [Required]
    public string Username { get; set; }

    [Required]
    public string Password { get; set; }
}
