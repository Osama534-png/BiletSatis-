using Microsoft.AspNetCore.Identity;

namespace BiletSatis.Web.Data;

/// <summary>
/// Identity'nin İngilizce hata mesajlarını ("Incorrect password." gibi) Türkçeye çevirir.
/// Program.cs'te AddErrorDescriber ile kaydedilir.
/// </summary>
public class TurkceIdentityHatalari : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => new()
    {
        Code = nameof(DefaultError),
        Description = "Bilinmeyen bir hata oluştu."
    };

    public override IdentityError PasswordMismatch() => new()
    {
        Code = nameof(PasswordMismatch),
        Description = "Mevcut şifreniz hatalı."
    };

    public override IdentityError PasswordTooShort(int length) => new()
    {
        Code = nameof(PasswordTooShort),
        Description = $"Şifre en az {length} karakter olmalıdır."
    };

    public override IdentityError PasswordRequiresDigit() => new()
    {
        Code = nameof(PasswordRequiresDigit),
        Description = "Şifre en az bir rakam içermelidir."
    };

    public override IdentityError PasswordRequiresLower() => new()
    {
        Code = nameof(PasswordRequiresLower),
        Description = "Şifre en az bir küçük harf içermelidir."
    };

    public override IdentityError PasswordRequiresUpper() => new()
    {
        Code = nameof(PasswordRequiresUpper),
        Description = "Şifre en az bir büyük harf içermelidir."
    };

    public override IdentityError PasswordRequiresNonAlphanumeric() => new()
    {
        Code = nameof(PasswordRequiresNonAlphanumeric),
        Description = "Şifre en az bir özel karakter içermelidir."
    };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => new()
    {
        Code = nameof(PasswordRequiresUniqueChars),
        Description = $"Şifre en az {uniqueChars} farklı karakter içermelidir."
    };

    public override IdentityError DuplicateEmail(string email) => new()
    {
        Code = nameof(DuplicateEmail),
        Description = $"'{email}' adresi başka bir hesapta kullanılıyor."
    };

    public override IdentityError DuplicateUserName(string userName) => new()
    {
        Code = nameof(DuplicateUserName),
        Description = $"'{userName}' adresi başka bir hesapta kullanılıyor."
    };

    public override IdentityError InvalidEmail(string? email) => new()
    {
        Code = nameof(InvalidEmail),
        Description = "Geçerli bir e-posta adresi girin."
    };

    public override IdentityError InvalidUserName(string? userName) => new()
    {
        Code = nameof(InvalidUserName),
        Description = "Kullanıcı adı geçersiz karakterler içeriyor."
    };
}
