# MafiaSandbox

Low-poly open-city crime sandbox. Originally prototyped as a single-file Three.js game
(`MafiaSandbox/Assets/Tools/Reference/html_game_index.html`); now being rebuilt in **Unity 6** step by step.

## Durum / Roadmap

| Adım | İçerik | Durum |
|---|---|---|
| 1 | Proje iskeleti, glTFast import, şehir yerleşimi (level builder), oyuncu / kamera / araç sürüş | ✅ bu commit |
| 2 | Karakter animasyon katmanı, silahlar, nişan, hasar / ragdoll | ⏳ |
| 3 | NavMesh + NPC yapay zekâsı (sivil / polis / çete / dükkâncı), aranma seviyesi | ⏳ |
| 4 | Trafik (yol grafiği + kavşak izinleri) | ⏳ |
| 5 | Ekonomi: envanter, dükkânlar, fiyat modeli, hırsızlık, soygun + HUD / minimap | ⏳ |
| 6 | Ev / araba, kamp, şantiye, hayvanlar, gün döngüsü, hava | ⏳ |

## Açmak için

1. Unity Hub ▸ **Add** ▸ `MafiaSandbox/` klasörünü seç (Unity 6.x). İlk açılışta Package Manager
   `com.unity.cloud.gltfast` paketini indirir ve 140 prop + 6 karakter GLB'sini import eder (1–2 dk).
2. Menü **MafiaSandbox ▸ 1. Setup (import settings)** — karakter GLB'lerini Legacy animasyona çevirir,
   gerekiyorsa *Active Input Handling = Both* yapar (bu durumda editörü bir kez yeniden başlat).
3. Menü **MafiaSandbox ▸ 2. Build City Scene** — `Assets/Data/city.json` + GLB'lerden `Assets/Scenes/City.unity`
   sahnesini kurar: zemin/yol/köprü mesh'leri (MeshCollider), 783 prop, 849 çarpışma kutusu, 15 bina işaretçisi,
   park halindeki araçlar, oyuncu, kamera, gün döngüsü.
4. ▶ Play. Oyuncu kuzeybatı platodaki harabe evin verandasında başlar; Midnight Regent önde park halinde.

### Kontroller (adım 1)

`WASD` hareket · `Shift` koşu · `C` çömel · `Space` zıpla · `V` birinci şahıs · fare ile bak (tıkla = imleç kilidi, `Esc` serbest)
`E` araca bin / in · araçta `W/S` gaz-fren, `A/D` direksiyon, `Space` el freni · `R` eve ışınlan

## Proje yapısı

```
MafiaSandbox/
  Assets/
    Data/        city.json (yerleşim), CityStatic.glb (zemin, yollar, köprü, üst geçit, tünel)
    Props/       140 low-poly prop / araç / bina kabuğu (Blender bpy ile üretildi)
    Characters/  rigli karakterler, 38 klip (Polis_01 oyuncu; tam set build_crowd.py ile üretilir)
    Scripts/
      Data/      CityData.cs   — city.json şeması + glTF→Unity koordinat dönüşümü (Coords)
      Player/    PlayerController, PlayerCamera, CharacterAnimator (Legacy Animation sarmalayıcı)
      Vehicles/  VehicleController (arcade rigidbody), VehicleInteraction (E bin/in)
      World/     BuildingInfo, CityCollider, RoadGraph, DayNightCycle
      Core/      GameManager          UI/ SimpleHud (geçici IMGUI HUD)
    Editor/      CityBuilder.cs — Setup + Build City Scene menüleri
    Tools/       Blender üretim script'leri, HTML prototipten dışa aktarım script'i, referans HTML oyun
  Packages/manifest.json   (glTFast 6.15)
```

### Koordinatlar

Prototip three.js (sağ el) dünyasında çalışır; glTFast import ederken **X eksenini ters çevirir**. `Coords.ToUnity`
aynı dönüşümü `city.json`'daki tüm konumlara uygular (`x → -x`, yaw `θ → -θ°`). Araç modellerinin burnu glTF'te +X
olduğu için `CityBuilder` modeli köke 90° döndürerek bağlar; araç kökünün +Z'si ileri yöndür.

### Veriyi yenilemek

HTML prototipte şehir değişirse: `Assets/Tools/export_city_from_html.py` ile `city.json` + `CityStatic.glb` yeniden
alınır, `Build City Scene` tekrar çalıştırılır. Prop'lar değişirse `build_props.py` çıktısı `Assets/Props/`e kopyalanır.
