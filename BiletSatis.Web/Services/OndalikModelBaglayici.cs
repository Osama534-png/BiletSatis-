using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BiletSatis.Web.Services;

/// <summary>
/// Formdan gelen ondalıklı sayıları noktalı (kültürden bağımsız) biçimde okur.
///
/// Sorun şuydu: HTML'de <c>&lt;input type="number"&gt;</c> alanı, tarayıcının dili ne
/// olursa olsun değeri her zaman <b>noktayla</b> gönderir — bu standartta böyle
/// tanımlı. Türkçe kültürde ise nokta binlik ayracıdır. Model bağlama sunucunun
/// kültürünü kullandığı için "250.50" değeri <b>25050</b> olarak okunuyordu:
/// yönetici 250,50 TL'lik bilet eklemek isterken 25.050 TL'lik bilet oluşuyordu.
///
/// Uygulamanın arayüz kültürü Türkçedir (fiyatlar "1.500 ₺" diye gösterilir); bu
/// yüzden kültürü tamamen değiştirmek yerine yalnızca <i>okuma</i> tarafı
/// kültürden bağımsız hâle getirildi. Gösterim Türkçe kalır, giriş standarda uyar.
/// </summary>
public class OndalikModelBaglayici : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sonuc = context.ValueProvider.GetValue(context.ModelName);
        if (sonuc == ValueProviderResult.None) return Task.CompletedTask;

        context.ModelState.SetModelValue(context.ModelName, sonuc);

        var metin = sonuc.FirstValue;
        if (string.IsNullOrWhiteSpace(metin))
        {
            // Boş değer: zorunluluk kararını Required doğrulaması versin.
            return Task.CompletedTask;
        }

        var hedefTur = Nullable.GetUnderlyingType(context.ModelType) ?? context.ModelType;

        // Önce kültürden bağımsız okunur (tarayıcının gönderdiği biçim budur).
        // Tutmazsa kullanıcının kültürü denenir; böylece elle "250,50" yazılan
        // bir değer de kaybolmaz.
        if (Coz(metin, hedefTur, CultureInfo.InvariantCulture, out var deger) ||
            Coz(metin, hedefTur, CultureInfo.CurrentCulture, out deger))
        {
            context.Result = ModelBindingResult.Success(deger);
            return Task.CompletedTask;
        }

        context.ModelState.TryAddModelError(context.ModelName, "Geçerli bir sayı girin.");
        return Task.CompletedTask;
    }

    private static bool Coz(string metin, Type hedefTur, CultureInfo kultur, out object? deger)
    {
        const NumberStyles bicim = NumberStyles.Float | NumberStyles.AllowThousands;

        if (hedefTur == typeof(decimal) && decimal.TryParse(metin, bicim, kultur, out var d))
        {
            deger = d;
            return true;
        }

        if (hedefTur == typeof(double) && double.TryParse(metin, bicim, kultur, out var db))
        {
            deger = db;
            return true;
        }

        if (hedefTur == typeof(float) && float.TryParse(metin, bicim, kultur, out var f))
        {
            deger = f;
            return true;
        }

        deger = null;
        return false;
    }
}

/// <summary>Ondalıklı türler için <see cref="OndalikModelBaglayici"/>'yı devreye alır.</summary>
public class OndalikModelBaglayiciSaglayicisi : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tur = Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType;

        return tur == typeof(decimal) || tur == typeof(double) || tur == typeof(float)
            ? new OndalikModelBaglayici()
            : null;
    }
}
