# MafiaSandbox

Low-poly open-city crime sandbox. Originally prototyped as a single-file Three.js game
(`MafiaSandbox/Assets/Tools/Reference/html_game_index.html`); now being rebuilt in **Unity 6** step by step.

## Durum / Roadmap

| Adım | İçerik | Durum |
|---|---|---|
| 1 | Proje iskeleti, glTFast import, şehir yerleşimi (level builder), oyuncu / kamera / araç sürüş | ✅ bu commit |
| 2 | Silahlar (6 prosedürel model), nişan / ateş / yakın dövüş, vuruş bölgeleri, ragdoll, yaralanma, yaya placeholder'ı, araç çarpması, prosedürel ses | ✅ |
| 3 | NavMesh + NPC yapay zekâsı (sivil kaç/saklan/merak, sağlıkçı, polis devriye-kovalama-tutuklama-ateş-arama, çete, dükkâncı eller yukarı), aranma seviyesi ★ | ✅ |
| 4 | Trafik: yol grafiğinde şerit takibi, kavşak izinleri, yaya/araç için frenleme, korna (yaya kenara çekilir), kuyruk, karşıdan gelene yol verme, sıkışınca geri çıkıp sollama, çıkmaz sokakta U dönüşü, görüş dışında kendini toparlama/yeniden konumlanma, duvarla kapalı yolların otomatik grafikten düşmesi (tünel), araçlar yere raycast ile oturur (yol çizgileri takılmaz) | ✅ |
| 5 | Ekonomi: nakit + sırt çantası (Tab), 22 eşya, 9 dükkân türü + evler/banka/karakol, raflardan satın alma / çalma (F / G), kasa (vault), silah dükkânı (silahlar artık satın alınır; başlangıç tabanca + bıçak), rehinci çalıntı malı %45'ten alır, fiyat modeli (kıtlık × gece × doygunluk × aranma), tanıklar (dükkâncı / ev sahibi / polis görürse ★, sessiz alarm → polis gelir), tabanca doğrultarak soygun (kasa parası), ceset arama, tutuklanınca çalıntı eşya el konur + ceza · HUD: can / stamina / açlık barları, nakit, minimap + büyük harita (M) | ✅ |
| 6 | Ev / araba, kamp, şantiye, hayvanlar, gün döngüsü, hava | ⏳ |

## Açmak için

1. Unity Hub ▸ **Add** ▸ `MafiaSandbox/` klasörünü seç (Unity 6.x). İlk açılışta Package Manager
   `com.unity.cloud.gltfast` paketini indirir ve 140 prop + 6 karakter GLB'sini import eder (1–2 dk).
2. Menü **MafiaSandbox ▸ 1. Setup (import settings)** — karakter GLB'lerini Legacy animasyona çevirir,
   gerekiyorsa *Active Input Handling = Both* yapar (bu durumda editörü bir kez yeniden başlat).
3. Menü **MafiaSandbox ▸ 2. Build City Scene** — `Assets/Data/city.json` + GLB'lerden `Assets/Scenes/City.unity`
   sahnesini kurar: zemin/yol/köprü mesh'leri (MeshCollider), 783 prop, 849 çarpışma kutusu, 15 bina işaretçisi,
   park halindeki araçlar, oyuncu, kamera, gün döngüsü; NavMesh'i fizik collider'larından pişirir (`City_NavMesh.asset`).
4. ▶ Play. Oyuncu kuzeybatı platodaki harabe evin verandasında başlar; Midnight Regent önde park halinde.

### Kontroller

`WASD` hareket · `Shift` koşu · `C` çömel · `Space` zıpla · `V` birinci şahıs · fare ile bak (tıkla = imleç kilidi, `Esc` serbest)
`1–6` silah (tabanca, tüfek, pompalı, bıçak, sopa, revolver) · sağ tık nişan · sol tık ateş / vuruş
`E` araca bin / in · araçta `W/S` gaz-fren, `A/D` direksiyon, `Space` el freni · `R` eve ışınlan · `H` yardım
Debug: `F1` +$500 · `F2` sıradaki dükkânın tezgâhına ışınlan · `F3` trafik durumunu `traffic_dump.txt` dosyasına yazar · `F4` öğlen yap / saati durdur · `F5` kuş bakışı kamera · `F6` araçların üstünde durum etiketleri · `F7` karakolun önüne ışınlan · `F8` +1 ★ · `F9` önüne hareketsiz yaya koyar · `F10` en yakın yayaya bakar · `F11` oyuncuyu öldürür

## Proje yapısı

```
MafiaSandbox/
  Assets/
    Data/        city.json (yerleşim), CityStatic.glb (zemin, yollar, köprü, üst geçit, tünel)
    Props/       140 low-poly prop / araç / bina kabuğu (Blender bpy ile üretildi)
    Characters/  rigli karakterler, 38 klip (Polis_01 oyuncu; tam set build_crowd.py ile üretilir)
    Scripts/
      Data/      CityData.cs   — city.json şeması + glTF→Unity koordinat dönüşümü (Coords)
      Player/    PlayerController, PlayerCamera (TPS/FPS/nişan), CharacterAnimator (Legacy, üst gövde katmanı)
      Combat/    WeaponDefs, WeaponModels (prosedürel), PlayerCombat (hitscan/saçma/yakın dövüş), CharacterBody
                 (can, vuruş kapsülleri, ragdoll, yaralanma), CombatFx (iz, namlu alevi, kan/kıvılcım)
      Npc/       NpcBrain (NavMeshAgent + algı + silah), CivilianBrain, PoliceBrain, GangBrain, KeeperBrain, NpcSpawner, Perception
      Core/      WantedSystem (★ 0–3, polis düşmanlığı, 25 sn'de sönme)
      Vehicles/  VehicleController (arcade rigidbody, yaya çarpması), VehicleInteraction (E bin/in)
      World/     BuildingInfo, CityCollider, RoadGraph, DayNightCycle
      Core/      GameManager (ölüm/yeniden doğma, log), Sfx (prosedürel ses)   UI/ SimpleHud (geçici IMGUI HUD)
    Editor/      CityBuilder.cs — Setup + Build City Scene menüleri
    Tools/       Blender üretim script'leri, HTML prototipten dışa aktarım script'i, referans HTML oyun
  Packages/manifest.json   (glTFast 6.15, AI Navigation 2.0)
```

### Koordinatlar

Prototip three.js (sağ el) dünyasında çalışır; glTFast import ederken **X eksenini ters çevirir**. `Coords.ToUnity`
aynı dönüşümü `city.json`'daki tüm konumlara uygular (`x → -x`, yaw `θ → -θ°`). Araç modellerinin burnu glTF'te +X
olduğu için `CityBuilder` modeli köke 90° döndürerek bağlar; araç kökünün +Z'si ileri yöndür.

### Veriyi yenilemek

HTML prototipte şehir değişirse: `Assets/Tools/export_city_from_html.py` ile `city.json` + `CityStatic.glb` yeniden
alınır, `Build City Scene` tekrar çalıştırılır. Prop'lar değişirse `build_props.py` çıktısı `Assets/Props/`e kopyalanır.
