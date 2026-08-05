using Microsoft.AspNetCore.Identity;

namespace BiletSatis.Web.Data;

public class ApplicationUser : IdentityUser
{
    public string Ad { get; set; } = "";
}
