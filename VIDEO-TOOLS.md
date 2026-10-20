# Video araçları

Fastdown 1.6 video sayfalarını ayrı süreç olarak çalışan yt-dlp ile çözümler. FFmpeg video ve ses akışlarını yeniden kodlamadan MP4/MKV kapsayıcısına birleştirir; yalnızca ses için MP3 veya M4A üretir. MP3 çıktısı ses dönüşümü içerir. Deno, yt-dlp'nin resmi paketiyle gelen YouTube JavaScript çözücüsünü çalıştırır. Harici shell komutu veya sayfanın verdiği süreç argümanı çalıştırılmaz; süreç argümanları uygulama tarafından ayrı değerler olarak oluşturulur. Kullanıcının yt-dlp yapılandırması ve harici eklentileri yüklenmez. Tarayıcı çerezleri okunmaz veya aktarılmaz.

Araçları kurmak/güncellemek için `Video-Araclarini-Guncelle.cmd` kullan. `scripts/Install-VideoTools.ps1`, aşağıdaki resmi GitHub depolarının yayınlarını ve SHA-256 bilgilerini alır, indirilen dosyaları doğrular ve yalnızca `Fastdown/tools` altına kurar:

- [yt-dlp](https://github.com/yt-dlp/yt-dlp/releases): kaynak kodu Unlicense; resmi PyInstaller Windows dağıtımındaki birleşik çalışma GPLv3+ içerir. Lisanslar `yt-dlp-LICENSE`, `yt-dlp-THIRD_PARTY_LICENSES.txt` ve `GPL-3.0.txt` içinde bulunur. Kaynak kodu ve derleme talimatları aynı depodadır.
- [FFmpeg-Builds](https://github.com/yt-dlp/FFmpeg-Builds): resmi yt-dlp FFmpeg derlemeleri, kullanılan paket `ffmpeg-master-latest-win64-gpl.zip`. FFmpeg ve FFprobe için GPLv3+ lisansı `yt-dlp-FFmpeg-Builds-LICENSE.txt` içinde bulunur. [Derleme kaynakları ve tarifleri](https://github.com/yt-dlp/FFmpeg-Builds), [FFmpeg kaynak kodu](https://github.com/FFmpeg/FFmpeg) ve [bu paketteki FFmpeg sürümünün kaynak kodu](https://github.com/FFmpeg/FFmpeg/tree/5993347a1b).
- [Deno](https://github.com/denoland/deno/releases): MIT; `deno-LICENSE.md`. Bağımlılık bildirimleri kaynak deposunda bulunur.

Kurulan yayın sürümleri, indirme adresleri ve paket hash değerleri `Fastdown/tools/versions.json` dosyasına kaydedilir. Araçların kaynak kodu kendi yayın etiketlerinden edinilebilir. Güncelleme sonrasında sürüm ve lisans kayıtları yeniden oluşturulur. Araçlar ayrı programlardır; Fastdown'ın HTTP indirme motoru bunlar olmadan da çalışır.

YouTube desteğinin teknik gereksinimleri: [yt-dlp bağımlılıkları](https://github.com/yt-dlp/yt-dlp#dependencies) ve [resmi EJS kurulum belgesi](https://github.com/yt-dlp/yt-dlp/wiki/EJS).
