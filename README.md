# Bekçi — Guard Tour API (Phase 1 Backend)

Güvenlik nöbetçileri (bekçiler) ve amirleri için çok kiracılı (multi-tenant) devriye
yönetim sistemi. Amirler rota ve kontrol noktaları tanımlar; bekçiler rotayı yürüyüp
her kontrol noktasında QR + GPS ile okutma yapar. Bu repo **Phase 1 backend**'idir
(dikey devriye döngüsü).

- **Tasarım / spec:** [`docs/superpowers/specs/2026-07-10-guard-tour-phase-1-design.md`](docs/superpowers/specs/2026-07-10-guard-tour-phase-1-design.md)
- **Uygulama planı:** [`docs/superpowers/plans/2026-07-10-guard-tour-phase-1-backend.md`](docs/superpowers/plans/2026-07-10-guard-tour-phase-1-backend.md)
- **Mobil (Flutter) planı:** [`docs/superpowers/plans/2026-07-10-guard-tour-phase-1-flutter.md`](docs/superpowers/plans/2026-07-10-guard-tour-phase-1-flutter.md) — henüz yazılmadı.

## Teknoloji

.NET 10 · Clean Architecture (Domain / Application / Infrastructure / Api) ·
EF Core + Npgsql/PostgreSQL · JWT auth · BCrypt · FluentValidation ·
Testler: xUnit + Testcontainers (Postgres) + FluentAssertions.

## Ön koşullar

| Araç | Neden |
|------|-------|
| **.NET 10 SDK** | Derleme ve çalıştırma |
| **Docker** (çalışır durumda) | Entegrasyon testleri Postgres'i Testcontainers ile ayağa kaldırır; API'yi lokal çalıştırmak için de kullanılır |

Kontrol: `dotnet --version` → `10.0.x`, `docker info` hatasız dönmeli.

## Derleme ve test

```bash
git clone <repo-url>
cd bekci_with_superpowers

dotnet build

# Tüm testler (domain unit + entegrasyon). Entegrasyon testleri için Docker AÇIK olmalı.
dotnet test
```

Beklenen: **27/27 test geçer.** İlk çalıştırmada Testcontainers `postgres:16-alpine`
imajını çeker (internet gerekir, ~1 dk). Önceden çekmek için: `docker pull postgres:16-alpine`.

> Sadece Docker gerektirmeyen domain birim testlerini çalıştırmak için:
> `dotnet test --filter "FullyQualifiedName~Domain"`

## API'yi lokal çalıştırma

API bir PostgreSQL'e ihtiyaç duyar (`src/Bekci.Api/appsettings.json` → `localhost:5432`).
Compose dosyası tam bu ayarlarla bir Postgres getirir:

```bash
docker compose up -d          # Postgres'i başlat (localhost:5432, db=bekci)
dotnet run --project src/Bekci.Api
```

Uygulama açılışta EF migration'larını otomatik uygular. Ardından:

- **Swagger UI:** `http://localhost:5xxx/swagger` (port konsolda yazar)
- **Sağlık/başlangıç:** kontrol noktaları için `POST /api/v1/auth/login` dışındaki tüm uçlar JWT ister.

Durdurmak / temizlemek: `docker compose down` (veriyi de silmek için `docker compose down -v`).

> **Not — ilk kullanıcı:** Phase 1'de kullanıcı/organizasyon oluşturan bir uç **yoktur**
> (kasıtlı kapsam dışı). Giriş yapıp uçları elle denemek için veritabanına doğrudan bir
> organizasyon + kullanıcı (BCrypt parola hash'i ile) eklemeniz gerekir. Referans olarak
> testlerdeki `tests/Bekci.Tests/Integration/AuthHelper.cs` seed akışına bakın
> (`User.Create(..., BCrypt.Net.BCrypt.HashPassword("..."), ...)`).

## Proje yapısı

```
src/
  Bekci.Domain/          # Entity'ler (Guid anahtarlı, rich domain) + geofence doğrulama
  Bekci.Application/     # DbContext (Repository), tenant query filter, servisler, DTO'lar
  Bekci.Infrastructure/  # EF/Npgsql kaydı + Migrations
  Bekci.Api/             # Controller'lar, JWT auth, Program.cs
tests/
  Bekci.Tests/           # Domain birim testleri + Testcontainers entegrasyon testleri
docs/superpowers/        # Spec + uygulama planları
```

## Kapsam (Phase 1)

**Var:** çok kiracılı auth (JWT, rol tabanlı) · amir CRUD (site/rota/checkpoint) ·
bekçi devriye akışı (idempotent başlatma, kritik **batch scan-ingestion** — sunucu
tarafı geo/order yeniden doğrulama, yumuşak geofence, idempotent tamamlama) · amir
devriye geçmişi.

**Önemli karar:** `OrderValid` = **Kural B (katı monotonik ilerleme)** — hem atlama hem
geri dönüş "sırasız" olarak işaretlenir.

**Sonraya bırakıldı (Phase 1.1 / Phase 2):** vardiya/atama modeli · SignalR canlı akış ·
panik butonu · not/fotoğraf · ProblemDetails global hata yönetimi (şu an 404/400 yerine
500) · `Scan(PatrolId,CheckpointId)` unique index · kullanıcı/organizasyon yönetimi uçları.

## Multi-tenancy

Her tenant-kapsamlı entity `TenantId` taşır ve JWT'deki `tenant_id` claim'inden beslenen
bir **EF Core global query filter** ile filtrelenir. Tek istisna login öncesi kullanıcı
aramasıdır (`IgnoreQueryFilters`). Kiracılar arası veri sızıntısı bu katmanda engellenir.
