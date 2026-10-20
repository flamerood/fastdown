# Başka bilgisayarda kurulum

Windows 10/11 x64 içindir. GitHub'daki **Releases** bölümünden `Fastdown-Windows-x64.zip` paketini indir; kaynak kodu ZIP'i uygulama paketi değildir.

1. ZIP'i tamamıyla kendi kullanıcının yazabildiği bir klasöre çıkar. ZIP'in içinden doğrudan çalıştırma. `Fastdown` ve `extension` klasörleri aynı üst klasörde kalsın.
2. **Baslat.cmd** veya `Fastdown/Fastdown.exe` dosyasını aç. Taşınabilir pakette .NET çalışma zamanı bulunur; Python, Node.js veya .NET kurman gerekmez.
3. **Eklenti-Kur.cmd** dosyasını bu bilgisayarda bir kez çalıştır. İşlem mevcut Windows hesabına köprü kaydeder; yönetici yetkisi istemez.
4. Chrome'da `chrome://extensions`, Edge'de `edge://extensions` aç. Geliştirici modunu aç, **Paketlenmemiş öğe yükle** ile paketin `extension` klasörünü seç.
5. Açık web sayfalarını yenile. Eklenti panelinde köprünün hazır/bağlı olduğunu doğrula.

Video araçları paketin `Fastdown/tools` klasöründedir. Hafif paket kullanıyorsan veya araçlar eksikse **Video-Araclarini-Guncelle.cmd** resmi araçları indirir. Bu işlem internete ihtiyaç duyar.

Paket taşınabilir; indirme listesi ve masaüstü ayarları her bilgisayarın `%LOCALAPPDATA%\Fastdown` klasöründe, eklenti tercihleri o tarayıcı profilinde tutulur. Kişisel liste ve oturum bilgileri dağıtım paketine dahil edilmez. Her yeni bilgisayarda indirme klasörü o kullanıcının Downloads klasörüne göre oluşturulur. Paketi başka klasöre taşırsan **Eklenti-Kur.cmd** dosyasını tekrar çalıştır.

Güncelleme sırasında Fastdown'ı kapat, yeni paketi ayrı klasöre çıkar ve köprüyü yeni klasörden kaydet. Mevcut eklentiyi aynı `extension` klasöründe güncellersen uzantılar sayfasında **Yenile** kullan. Eklentiyi kaldırıp yeniden eklemek tarayıcıdaki tercihleri silebilir.

Dosyalar imzalı değildir; Windows indirdiğin uygulamanın yayıncısını doğrulayamayabilir. Paketin SHA-256 değerini aynı sürümün `.sha256` dosyasıyla karşılaştırabilirsin. Destek: [eklenti kurulumu](extension/KURULUM.md), [video araçları](VIDEO-TOOLS.md).
