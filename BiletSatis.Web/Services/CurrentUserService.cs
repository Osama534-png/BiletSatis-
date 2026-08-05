using System.Security.Claims;

namespace BiletSatis.Web.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUserService(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public string GetKullaniciId()
    {
        var kullaniciId = _accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(kullaniciId))
        {
            throw new InvalidOperationException("Kimliği doğrulanmış kullanıcı bulunamadı.");
        }

        return kullaniciId;
    }
}
