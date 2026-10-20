# Fastdown tarayıcı eklentisi

Chrome ve Microsoft Edge için Manifest V3 eklentisi. Mağazada yayımlanmadığı için ilk kurulumda paketlenmemiş eklenti olarak yüklenir.

1. Ana klasördeki **Eklenti-Kur.cmd** dosyasını çalıştır. Bu işlem sadece kendi Windows hesabında Fastdown'ın yerel köprüsünü kaydeder; yönetici yetkisi gerekmez.
2. Chrome'da `chrome://extensions`, Edge'de `edge://extensions` sayfasını aç.
3. **Geliştirici modu** seçeneğini aç, **Paketlenmemiş öğe yükle** düğmesine bas ve bu **extension** klasörünü seç.
4. Açık web sayfalarını yenile. Araç çubuğundaki Fastdown simgesine tıkla. “Köprü hazır” veya “Fastdown açık ve bağlı” yazmalı.

## Kullanım

- Normal dosya indirmeleri otomatik olarak Fastdown'a aktarılır. Otomatik yakalamayı eklenti panelinden kapatabilirsin.
- Video veya yeterince büyük bir resmin üzerine fareyle gelince **↓ Fastdown ile indir** düğmesi çıkar.
- Düğmenin yanındaki **×**, o medyayı yalnızca o sayfa açıkken gizler. Üstüne yeniden gelmek veya aynı kaynağın yeniden eklenmesi düğmeyi geri getirmez; sayfayı yenileyince tekrar görünür. Bir videonun kalite değiştirmesi gizlemeyi kaldırmaz.
- Kalıcı gizleme için eklenti panelinde **Sayfa / site için düğmeleri gizle** bölümünü aç. **Bu sayfada gösterme** yalnızca açık adresi; **Bu sitede gösterme** gösterilen site adresinin tüm sayfalarını kapsar. Seçimler tarayıcı kapanıp açıldığında da korunur. Aynı bölümdeki listeden **Kaldır** ile geri açabilirsin.
- Videoda bu düğme **Video ve kalite seç** listesini açar. Bulunan **1080p, 720p** gibi seçeneklerden istediğin satıra bas; yalnızca o dosya indirilir. Resimler doğrudan aktarılır.
- Resim, video, ses veya bağlantıya sağ tıkla: **Fastdown ile indir**.
- Eklenti simgesi sayfadaki doğrudan medya dosyalarını listeler; video, resim ve ses filtrelerinden birini seçebilirsin.
- Paneldeki **Video kalitesi** filtresiyle örneğin yalnızca 720p dosyalarını gösterip **İndir** düğmesine basabilirsin.
- Fastdown kapalıysa indirme eklediğinde otomatik açılır.
- **YouTube / Shorts:** panelde **Kaliteleri getir**, çözünürlüğü seç ve **Videoyu indir** düğmesine bas. YouTube üzerindeki video düğmesi de aynı kalite seçeneklerini açar. Ayrı video ve ses dosyaları tek MKV dosyasında birleştirilir. YouTube bağlantısını panelin bağlantı alanına yapıştırmak da kalite seçimini açar.
- Diğer desteklenen video sitelerinde **Bu sayfadaki videoyu çözümle → Kaliteleri getir** yolunu dene. Masaüstünde de **Yeni indirme → Video sayfası** seçeneği kullanılabilir.

Tarayıcıdaki indirme önce duraklatılır. Fastdown bağlantıyı kontrol edip listeye kaydettikten sonra tarayıcıdaki kopya iptal edilir. Köprüye ulaşılamazsa veya sunucu bağlantıyı reddederse tarayıcıdaki indirme devam ettirilir. POST ile oluşturulan, gizli sekmeye ait, başka eklentinin başlattığı veya tarayıcının tehlikeli işaretlediği indirmeler otomatik devredilmez.

## Destek sınırları

1.3 sürümü tek bir site veya oynatıcıya bağlı değildir. Fetch/XHR ile alınan JSON kaynak listeleri, sıradan gömülü kaynak ayarları, HTML `<source>` seçenekleri ve yaygın oynatıcıların açık kaynak API'leri taranır. Kaynak listesinde verilen oynatılmamış sürümler de seçilebilir. Çapraz kökenli iframe içindeki kaynaklar ve gerçek video boyutları eklenti panelinde birleştirilir.

Kalite etiketi olmayan dosyalarda MP4/WebM başlıkları küçük HTTP Range istekleriyle okunur. MP4 medya verisi sonda bulunan başlığa erişmek için indirilmez; gerekli atoma atlanır. Her bilinmeyen dosya için en fazla üç adet 128 KB aralık okunur. Sunucu Range'i yok saysa da yanıtın tamamı indirilmez. URL'den çıkarılan bilgi **Bağlantı etiketi** olarak belirtilir. Dosya başlığı erişilemiyor veya biçim çözülemiyorsa **Kalite bilinmiyor** kalabilir. CSS görüntü boyutu çözünürlük olarak kullanılmaz; özellik video kalitesini yükseltmez veya dönüştürmez.

1.6 güncellemesinden sonra tarayıcının uzantılar sayfasında Fastdown eklentisini **Yenile**, ardından açık video sayfalarını da yenile. Kaynak gözlemcisi sayfa başlarken çalıştığı için açık sayfanın yenilenmesi önemlidir. Sabit eklenti kimliği değişmez; Fastdown için yeni klasöre geçtiysen Eklenti-Kur.cmd dosyasını yeniden çalıştır.

Doğrudan HTTP/HTTPS video, resim ve ses dosyaları indirilebilir. Blob oynatıcının kaynak listesinde doğrudan dosyalar bulunursa bunlar da listelenir. HLS ana listelerindeki varyantlar ve DASH temsilleri çözünürlükleriyle algılanır. Bu ham kaynakların doğrudan aktarımı yerine **video sayfası çözümleme** kullanılabilir; yt-dlp'nin desteklediği sayfalar/yayınlar indirilebilir ve sesle birleştirilebilir. Her site veya yayın desteklenmez. DRM çözme veya anahtar toplama yapılmaz. Oturum çerezleri aktarılmaz; giriş, bot doğrulaması, ücretli erişim veya tarayıcı oturumu isteyen videolar desteklenmez. Devam eden canlı yayınlar ve oynatma listeleri desteklenmez. Site değişikliklerinde kök klasördeki **Video-Araclarini-Guncelle.cmd** ile video araçlarını güncelle. Ayrıntılar: [video araçları](../VIDEO-TOOLS.md).

Düğmeler 96×64 pikselden büyük resim/video öğelerinde görünür. Küçük resimler için sağ tık menüsünü kullan. Dinamik eklenen medya, responsive resimler ve HTTP/HTTPS çerçeveleri desteklenir. Tarayıcı ayarları, Chrome Web Store gibi tarayıcının izin vermediği sayfalara eklenti düğmesi eklenemez.

Gizleme ayarları resim/video üzerindeki düğmelere uygulanır. Sayfa gizlenirse içindeki çapraz kökenli gömülü oynatıcıların düğmeleri de gizlenir. Sayfa karşılaştırmasında adresin sorgu alanları korunur (örneğin YouTube video kimliği); `#` bölüm işaretleri dikkate alınmaz. Site ayarı panelde görünen protokol, alan adı ve varsa port için geçerlidir. Gizlenen sayfa/site adresleri yalnızca eklentinin yerel ayarlarında saklanır; geçici × seçimleri diske yazılmaz. Paneldeki medya listesi ve sağ tıkla indirme kullanılabilir.

## İzinler ve yerel veri

Tüm HTTP/HTTPS siteleri izni medya öğelerini bulmak ve ağdaki medya yanıtlarını tespit etmek için; downloads indirmeyi duraklatıp devretmek için; nativeMessaging sadece Fastdown köprüsü için kullanılır. Sayfa bağlamındaki gözlemci mevcut Fetch/XHR yanıtlarının sınırlı kopyalarından sadece video URL'si, kalite, dosya boyutu ve biçim alanlarını çıkarır; sayfanın isteklerini veya yanıtlarını değiştirmez. Çerez, parola ve diğer yanıt alanları eklentiye aktarılmaz. Sayfadan gelen keşif mesajları yalnızca medya listesine eklenebilir; indirmeyi kendiliğinden başlatamaz. Algılanan liste tarayıcı oturumu boyunca, sekme başına en fazla 80 kayıt tutulur ve sekme kapanınca temizlenir. Telemetri veya harici analiz sunucusu kullanılmaz.

Native Messaging kaydı yalnızca bu eklentinin sabit kimliğini kabul eder. Masaüstü bağlantısı aynı Windows kullanıcısıyla sınırlı bir named pipe kullanır; ağ portu açmaz.

Uygulama klasörünü taşırsan **Eklenti-Kur.cmd** dosyasını yeni konumda tekrar çalıştır. Kaldırmak için **Eklenti-Kaldir.cmd** dosyasını çalıştır ve tarayıcıdaki Fastdown eklentisini kaldır.

Teknik belgeler: [Chrome Native Messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging), [Downloads API](https://developer.chrome.com/docs/extensions/reference/api/downloads).

## 1.6: Biçim seçimi ve filtreler

Video sayfası çözümleme panelinde ve YouTube üzerindeki kalite menüsünde MP4/MKV video + ses veya MP3/M4A yalnızca ses seçebilirsin. Ses biçiminde çözünürlük seçimi kullanılmaz.

Paneldeki medya filtresi küçük dosyaları, kısa videoları ve küçük resimleri gizler. Boyut, süre ve resim ölçüsü eşiklerini değiştirebilir veya filtreyi tamamen kapatabilirsin. Bilinmeyen ölçüler tahmin edilmez. **Filtrelenenleri göster** ile gizlenen öğeleri de görüp indirebilirsin; bu geçici görünüm her panel açılışında sıfırlanır. Filtre tercihleri tarayıcıda saklanır.
