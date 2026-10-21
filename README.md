# Fastdown

IDM'den esinlenen, Türkçe arayüzlü Windows indirme yöneticisi. WPF ve .NET 10 ile geliştirilmiştir. Harici NuGet paketine ihtiyaç duymaz.

**[Windows için Fastdown-Setup.exe indir](https://github.com/flamerood/fastdown/releases/latest/download/Fastdown-Setup.exe)** · [Tüm sürümler ve taşınabilir ZIP](https://github.com/flamerood/fastdown/releases)

## 1.7: Güncelleme, öncelik ve hız

- **Tek EXE kurulum:** Kullanıcı hesabına kurulur; yönetici yetkisi istemez. Çalışma zamanı, video araçları, kısayollar ve tarayıcı köprüsü dahildir. Tarayıcı eklentisi için kurulum rehberi açılabilir.
- **Otomatik güncelleme:** Başlangıçta ve uygulama açıkken 12 saatte bir kararlı GitHub sürümü kontrol edilir. Yeni kurulum dosyası indirilip GitHub'ın SHA-256 kaydıyla doğrulanır. **Kur ve yeniden başlat** düğmesi indirmeleri duraklatır, listeyi kaydeder ve kurulumu başlatır. Ayarlar ve indirme geçmişi korunur. Ayarlar'dan otomatik kontrolü kapatabilir veya elle kontrol edebilirsin.
- **Kuyruk önceliği:** Sırada bekleyen dosyaları sürükleyerek sırala veya sağ tık menüsünden öne/yukarı/aşağı taşı. Açık indirme kesilmez; sıra yeniden açılışta korunur.
- **Hız sınırı:** Ayarlar'dan toplam, dosyaya sağ tıklayarak dosya başına sınır koy. 0 = sınırsız; en küçük sınır 16 KB/sn. Dosyanın tüm bağlantıları aynı bütçeyi paylaşır. HTTP indirmeleri yeni sınıra canlı geçer; video işleri yeni sınırla otomatik duraklatılıp sürdürülür. Her iki sınır varsa daha dar bütçe uygulanır.

1.6'dan geçerken kurulum dosyasını bir kez indir. 1.7 ve sonraki sürümlerde uygulama içi güncelleme kullanılabilir. Güncellemeden sonra tarayıcı uzantısını ve açık sayfaları yenile. Kurulum bir sistem yeniden başlatması istemez.

## Çalıştırma

`Fastdown` klasöründeki **Fastdown.exe** dosyasını aç. Klasördeki diğer dosyaları aynı yerde tut.

Gereksinim: Windows 10/11 x64. `Fastdown-Windows-x64.zip` çalışma zamanını ve video araçlarını içerir; ayrıca .NET, Python veya Node.js kurmak gerekmez. Başka bilgisayarlar için [kurulum rehberi](KURULUM.md).

1. **Yeni indirme** düğmesine bas veya `Ctrl+N` kullan.
2. Doğrudan HTTP/HTTPS dosya bağlantısını gir. Panodaki uygun bağlantı otomatik doldurulur.
3. Dosya adını ve kaydedilecek klasörü seçip **İndirmeyi başlat** düğmesine bas.
4. Satırdaki duraklat/sürdür düğmesini kullan. Klasör simgesi dosyanın konumunu açar.

## Özellikler

- Gerçek HTTP/HTTPS indirme; dosya başına 1–8 bağlantı.
- Kuyruk ve 1–5 eşzamanlı indirme.
- Duraklatma, sürdürme ve bütün indirmelere toplu işlem.
- Toplam hız sınırı; hız, ilerleme, tahmini kalan süre ve dosya boyutu.
- Dosya adı/bağlantı araması, durum ve dosya türü filtreleri.
- Kapatırken indirmeleri güvenle duraklatma ve listeyi kaydetme.
- Aynı dosya adına numara ekleyerek mevcut dosyaları koruma.
- Hatalı indirmeleri yeniden deneme; geçici bağlantı hatalarında otomatik tekrar.
- Tamamlanmış dosyayı silmeden indirme geçmişinden kaldırma.
- Chrome / Edge eklentisiyle otomatik indirme yakalama, video/resim üzerinde indirme düğmesi ve sağ tık menüsü.
- Genel Fetch/XHR kaynak keşfiyle oynatılmamış kaliteleri bulma, iframe kaynaklarını birleştirme, etiketsiz MP4/WebM dosyalarının gerçek çözünürlüğünü başlıktan okuma ve kalite filtresi. HLS/DASH kaliteleri de algılanır.
- YouTube, Shorts ve yt-dlp'nin desteklediği video sayfalarında kalite seçimi; MP4/MKV video + ses veya yalnızca MP3/M4A ses. Video yeniden kodlanmaz, MP3 ses dönüştürülür. Sayfa, kalite ve biçim saklanır; süreli bağlantılar her indirmede yeniden çözülür.
- Medya düğmesindeki **×** ile o medyayı sayfa yenilenene kadar gizleme; panelden kalıcı sayfa/site istisnaları ve bunları kaldırabileceğin liste. Gömülü oynatıcılar ve sayfa yenilemeden gezinme de bu ayarlara uyar.
- Eklentide boyut, süre ve resim ölçüsü filtreleri. **Filtrelenenleri göster** ile gizlenen medyayı da seçip indirebilirsin; filtreler isteğe bağlıdır.
- Tamamlanınca Windows bildirimi, klasörü veya desteklenen dosyayı açma; bu oturum için kuyruk bitince bilgisayarı kapatma. Kapatma öncesinde 60 saniye boyunca iptal edebilirsin; duraklatılmış/hatalı işler kapanmayı engeller.

## YouTube ve video sayfaları

Masaüstünde **Yeni indirme** bölümüne video sayfası bağlantısını yapıştır. YouTube otomatik tanınır; diğer desteklenen siteler için **Video sayfası** kutusunu işaretle. **Kaliteleri getir**, çözünürlüğü seç ve **İndirmeyi başlat** düğmesine bas. Seçtiğin kalite yoksa başka bir kaliteye sessizce geçilmez.

Eklenti panelinde **YouTube videosunu indir / Bu sayfadaki videoyu çözümle → Kaliteleri getir → kalite ve biçim seç → İndir** yolunu kullan. YouTube'da video üstündeki Fastdown düğmesi de bu listeyi açar. MP3/M4A seçildiğinde çözünürlük seçimi kapanır. YouTube bağlantısını panelin bağlantı alanına yapıştırmak da kalite seçimini açar. Liste ve kanal bağlantıları yerine tek video bağlantısı kullan. Doğrudan resim/dosya indirmelerinde kaynak biçim korunur.

Gerekli yt-dlp, FFmpeg ve Deno araçları `Fastdown/tools` klasöründedir. Site değişikliklerinden sonra **Video-Araclarini-Guncelle.cmd** ile resmi sürümleri SHA-256 doğrulamasıyla güncelleyebilirsin. Güncelleme yalnızca uygulamanın kendi klasörüne yapılır; PATH veya sistem ayarları değiştirilmez. Güncellemeden önce video indirmelerini duraklat.

Herkese açık, erişilebilir videolar desteklenir. DRM, devam eden canlı yayınlar, tarayıcı çerezlerini aktarma ve giriş/doğrulama isteyen videolar desteklenmez. Sayfa çözümleyici bazı HLS/DASH yayınlarını da indirebilir; ağdan algılanan her ham yayın parçasının doğrudan aktarılabileceği anlamına gelmez. Video indirmelerinde toplam sınır eşzamanlı indirme sayısına bölünür; dosya sınırı varsa daha düşük olan değer uygulanır. Araçların sürümleri ve üçüncü taraf lisansları için [VIDEO-TOOLS.md](VIDEO-TOOLS.md).

Varsayılan indirme klasörü: `%USERPROFILE%\Downloads\Fastdown`.
Liste, ayarlar ve geçici parçalar: `%LOCALAPPDATA%\Fastdown`. Önceki Akış kurulumunun listesi varsa aynı yerel kayıt kullanılmaya devam edilir.

## Devam desteği

Sunucu HTTP Range ve ETag veya Last-Modified bilgisi sağlıyorsa parçalar kaldığı yerden sürer. Dosya sunucuda değişmişse eski parçalar atılır ve indirme yeniden başlar. Sunucu devamı desteklemiyorsa tek bağlantı kullanılır; duraklatılmış dosya baştan indirilir. Bilinmeyen boyutlu ve boş dosyalar da desteklenir.

Parçalar önce geçici alana yazılır, boyutları doğrulanır ve hedef dosya tamamlanınca oluşturulur. Bu nedenle büyük indirmeler için hem geçici alanda hem hedef diskte yeterli boş yer gerekir. Uygulama yeniden açıldığında önceki indirmeler duraklatılmış kalır; **Tümünü sürdür** ile devam edebilirsin.

Tarayıcı bağlantısı için **Eklenti-Kur.cmd** dosyasını çalıştır ve Chrome/Edge uzantılar sayfasından **extension** klasörünü paketlenmemiş eklenti olarak yükle. Ayrıntılar: [eklenti kurulumu](extension/KURULUM.md). Güncellemeden sonra uzantılar sayfasında Fastdown'ı ve açık video sayfasını yenile. İndirme listesi bağlantıları ve gerekli kaynak sayfa bilgisi yerel JSON dosyasında saklar.

## Geliştirme

.NET 10 SDK ve tarayıcı testleri için Node.js gerekir. Paket oluşturma Windows üzerinde yapılır.

```powershell
npm ci
npx playwright install chromium
dotnet build src/Fastdown.App/Fastdown.App.csproj -c Release
dotnet run --project tests/Fastdown.Tests/Fastdown.Tests.csproj -c Release
# Video araçları kuruluysa gerçek yerel DASH indirme/birleştirme testleri:
dotnet run --project tests/Fastdown.Tests/Fastdown.Tests.csproj -c Release -- --video-suite
dotnet publish src/Fastdown.App/Fastdown.App.csproj -c Release --self-contained false -o Fastdown
dotnet publish src/Fastdown.BrowserHost/Fastdown.BrowserHost.csproj -c Release --self-contained false -o Fastdown
node --test tests/browser/extension.test.mjs
node --test tests/browser/discovery.test.mjs
node tests/browser/overlay-e2e.mjs
node tests/browser/filter-e2e.mjs
# Başka bilgisayarlara dağıtılacak çalışma zamanı dahil ZIP:
./scripts/Build-Release.ps1
```

ZIP, `Fastdown-Setup.exe` ve SHA-256 dosyaları `artifacts` altında oluşur. Resmi, imzası ve hash değeri doğrulanan Inno Setup derleyicisi ilk çalışmada `artifacts/build-tools` içine hazırlanır; yalnızca ZIP için `-SkipSetup` kullan. Video araçları eksikse resmi yayınlardan doğrulanarak indirilir. GitHub Actions testleri push/PR sırasında, **Portable Windows package** iş akışı ise elle çalıştırıldığında iki paketi üretir. Kişisel ayarlar, indirme listeleri ve derlenmiş dosyalar Git'e dahil edilmez.

Testler yerel HTTP sunucusu kullanır, internetten dosya indirmez. Çok parçalı indirme ve SHA-256 doğrulaması, Range desteklemeyen sunucu, bilinmeyen boyut, boş dosya, kapanıp açıldıktan sonra sürdürme, değişen ETag, kuyruk, toplam hız sınırı, HTTP hataları ve mevcut dosyaların korunması sınanır. `--video-suite` gerçek yt-dlp/FFmpeg ile iki çözünürlüklü yerel DASH kaynağından seçilen videoyu ve sesi indirir; ffprobe ile gerçek çözünürlüğü ve ses akışını doğrular. `node tests/browser/video-smoke.mjs` aynı işlemi sıradan HTML video sayfasında sınar. `node tests/browser/e2e.mjs` izole Chromium profilinde eklentiyi ve yerel köprüyü sınar; önce Fastdown'ı kapat.

Arayüz önizlemesi için:

```powershell
Fastdown\Fastdown.exe --preview artifacts\preview.png
```

Kaynaklar: `src/Fastdown.Core` indirme motoru, `src/Fastdown.App` masaüstü arayüzü, `tests/Fastdown.Tests` bağımsız entegrasyon testleri.
