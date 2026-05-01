using System.ComponentModel.DataAnnotations;

namespace UmageAI.Optimizely.GraphSearchTools.SampleSite.Models;

public class LoginViewModel
{
    [Required]
    public string Username { get; set; }

    [Required]
    public string Password { get; set; }
}
