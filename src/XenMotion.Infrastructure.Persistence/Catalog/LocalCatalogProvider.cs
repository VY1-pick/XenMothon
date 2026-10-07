using System.Text.RegularExpressions;
using XenMotion.Core.Enums;
using XenMotion.Core.Interfaces;
using XenMotion.Core.Models.Catalog;
using Microsoft.Extensions.Logging;

namespace XenMotion.Infrastructure.Persistence.Catalog;

/// <summary>
/// Deterministic in-memory catalog provider supplying realistic sample categories, wallpapers,
/// search indexing, filters, sorting, and downloadable mock media streams without requiring internet access.
/// </summary>
public sealed class LocalCatalogProvider : ICatalogProvider
{
    private readonly ILogger<LocalCatalogProvider> _logger;
    private readonly List<CatalogItem> _items;
    private readonly List<CatalogCategory> _categories;

    public LocalCatalogProvider(ILogger<LocalCatalogProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _categories =
        [
            new CatalogCategory("all", "All Wallpapers", "Explore all community and curated wallpapers", "\uE71D", 24),
            new CatalogCategory("nature", "Nature & Landscapes", "Scenic mountains, oceans, forests, and seasons", "\uE707", 4),
            new CatalogCategory("cyberpunk", "Cyberpunk & Sci-Fi", "Futuristic cityscapes, synthwave, neon, and high-tech", "\uE790", 4),
            new CatalogCategory("space", "Space & Astronomy", "Nebulas, starfields, galaxies, and celestial phenomena", "\uE706", 3),
            new CatalogCategory("abstract", "Abstract & Motion", "Fluid art, geometric shapes, particles, and minimal lines", "\uE790", 3),
            new CatalogCategory("gaming", "Gaming & Retro", "Pixel art, fantasy worlds, mechs, and retro consoles", "\uE7FC", 3),
            new CatalogCategory("anime", "Anime & Illustration", "Aesthetic hand-drawn scenes, lofi, and anime landscapes", "\uE8B9", 3),
            new CatalogCategory("minimal", "Minimal & Clean", "Subtle gradients, monochromatic, and uncluttered views", "\uE7C5", 2),
            new CatalogCategory("cities", "Cities & Architecture", "Modern skylines, rainy avenues, and architectural wonders", "\uE80F", 2)
        ];

        _items =
        [
            // Nature
            new CatalogItem
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                Title = "Alpine Dawn in 4K",
                Description = "Breathtaking morning sunlight sweeping across snowcapped alpine peaks with gentle fog movement.",
                Author = "AeroVisuals",
                ThumbnailUrl = "assets/catalog/alpine_dawn_thumb.jpg",
                PreviewUrl = "assets/catalog/alpine_dawn_preview.mp4",
                Type = WallpaperType.Video,
                Width = 3840,
                Height = 2160,
                Duration = TimeSpan.FromSeconds(20),
                FileSizeBytes = 42 * 1024 * 1024,
                Tags = ["alpine", "mountains", "sunrise", "nature", "4k", "fog"],
                CategoryId = "nature",
                CategoryName = "Nature & Landscapes",
                Rating = 4.92,
                RatingCount = 1420,
                DownloadsCount = 14200,
                CreatedAtUtc = new DateTime(2026, 1, 15, 8, 30, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 1, 15, 8, 30, 0, DateTimeKind.Utc),
                IsFeatured = true
            },
            new CatalogItem
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
                Title = "Deep Forest Rain",
                Description = "Gentle rain falling through lush pine canopies with soothing misty atmosphere.",
                Author = "RainforestAudio",
                ThumbnailUrl = "assets/catalog/forest_rain_thumb.jpg",
                PreviewUrl = "assets/catalog/forest_rain_preview.mp4",
                Type = WallpaperType.Video,
                Width = 2560,
                Height = 1440,
                Duration = TimeSpan.FromSeconds(15),
                FileSizeBytes = 28 * 1024 * 1024,
                Tags = ["rain", "forest", "nature", "peaceful", "green", "trees"],
                CategoryId = "nature",
                CategoryName = "Nature & Landscapes",
                Rating = 4.80,
                RatingCount = 980,
                DownloadsCount = 9800,
                CreatedAtUtc = new DateTime(2026, 2, 1, 14, 0, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 2, 1, 14, 0, 0, DateTimeKind.Utc),
                IsFeatured = false
            },
            new CatalogItem
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000003"),
                Title = "Aurora Borealis Night",
                Description = "Stunning emerald and violet polar lights dancing above a crystal-clear frozen fjord.",
                Author = "NordicCapture",
                ThumbnailUrl = "assets/catalog/aurora_thumb.jpg",
                PreviewUrl = "assets/catalog/aurora_preview.png",
                Type = WallpaperType.Image,
                Width = 3840,
                Height = 2160,
                FileSizeBytes = 8 * 1024 * 1024,
                Tags = ["aurora", "night", "northern lights", "arctic", "4k", "green"],
                CategoryId = "nature",
                CategoryName = "Nature & Landscapes",
                Rating = 4.95,
                RatingCount = 1850,
                DownloadsCount = 18500,
                CreatedAtUtc = new DateTime(2026, 2, 10, 22, 15, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 2, 10, 22, 15, 0, DateTimeKind.Utc),
                IsFeatured = true
            },
            new CatalogItem
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000004"),
                Title = "Sunset Over Mountain Lake",
                Description = "Golden hour reflections casting deep orange hues over a peaceful mountain lake.",
                Author = "SolitudeShots",
                ThumbnailUrl = "assets/catalog/sunset_lake_thumb.jpg",
                PreviewUrl = "assets/catalog/sunset_lake_preview.png",
                Type = WallpaperType.Image,
                Width = 1920,
                Height = 1080,
                FileSizeBytes = 4 * 1024 * 1024,
                Tags = ["sunset", "lake", "reflection", "mountains", "orange", "calm"],
                CategoryId = "nature",
                CategoryName = "Nature & Landscapes",
                Rating = 4.65,
                RatingCount = 520,
                DownloadsCount = 5200,
                CreatedAtUtc = new DateTime(2026, 2, 18, 17, 45, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 2, 18, 17, 45, 0, DateTimeKind.Utc),
                IsFeatured = false
            },

            // Cyberpunk
            new CatalogItem
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000001"),
                Title = "Neo Tokyo Rainy Street",
                Description = "Vibrant neon-lit cyber city alley with reflections on wet asphalt and flying vehicles.",
                Author = "KuroNeko",
                ThumbnailUrl = "assets/catalog/neo_tokyo_thumb.jpg",
                PreviewUrl = "assets/catalog/neo_tokyo_preview.mp4",
                Type = WallpaperType.Video,
                Width = 3840,
                Height = 2160,
                Duration = TimeSpan.FromSeconds(25),
                FileSizeBytes = 55 * 1024 * 1024,
                Tags = ["cyberpunk", "tokyo", "neon", "rain", "futuristic", "4k"],
                CategoryId = "cyberpunk",
                CategoryName = "Cyberpunk & Sci-Fi",
                Rating = 4.94,
                RatingCount = 2300,
                DownloadsCount = 23000,
                CreatedAtUtc = new DateTime(2026, 1, 20, 19, 0, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 1, 20, 19, 0, 0, DateTimeKind.Utc),
                IsFeatured = true
            },
            new CatalogItem
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000002"),
                Title = "Synthwave Highway Sunset",
                Description = "Classic 80s outrun wireframe grid with neon sun and retro sports car cruising into infinity.",
                Author = "RetrowaveDreamer",
                ThumbnailUrl = "assets/catalog/synthwave_thumb.jpg",
                PreviewUrl = "assets/catalog/synthwave_preview.mp4",
                Type = WallpaperType.Video,
                Width = 2560,
                Height = 1440,
                Duration = TimeSpan.FromSeconds(18),
                FileSizeBytes = 31 * 1024 * 1024,
                Tags = ["synthwave", "outrun", "retro", "neon", "car", "sunset"],
                CategoryId = "cyberpunk",
                CategoryName = "Cyberpunk & Sci-Fi",
                Rating = 4.88,
                RatingCount = 1640,
                DownloadsCount = 16400,
                CreatedAtUtc = new DateTime(2026, 2, 5, 20, 30, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 2, 5, 20, 30, 0, DateTimeKind.Utc),
                IsFeatured = true
            },
            new CatalogItem
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000003"),
                Title = "Cybernetic Samurai",
                Description = "High-tech augmented warrior standing on a skyscraper rooftop in a stormy dystopia.",
                Author = "GhostInMatrix",
                ThumbnailUrl = "assets/catalog/samurai_thumb.jpg",
                PreviewUrl = "assets/catalog/samurai_preview.png",
                Type = WallpaperType.Image,
                Width = 3840,
                Height = 2160,
                FileSizeBytes = 7 * 1024 * 1024,
                Tags = ["samurai", "cyberpunk", "katana", "sci-fi", "4k", "dark"],
                CategoryId = "cyberpunk",
                CategoryName = "Cyberpunk & Sci-Fi",
                Rating = 4.78,
                RatingCount = 1120,
                DownloadsCount = 11200,
                CreatedAtUtc = new DateTime(2026, 2, 12, 11, 0, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 2, 12, 11, 0, 0, DateTimeKind.Utc),
                IsFeatured = false
            },
            new CatalogItem
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000004"),
                Title = "Neon Hologram Alley",
                Description = "Panoramic ultrawide view of holographic advertisements in a vibrant dystopian metropolis.",
                Author = "AeroVisuals",
                ThumbnailUrl = "assets/catalog/holo_alley_thumb.jpg",
                PreviewUrl = "assets/catalog/holo_alley_preview.png",
                Type = WallpaperType.Image,
                Width = 3440,
                Height = 1440,
                FileSizeBytes = 9 * 1024 * 1024,
                Tags = ["ultrawide", "cyberpunk", "hologram", "city", "neon", "panoramic"],
                CategoryId = "cyberpunk",
                CategoryName = "Cyberpunk & Sci-Fi",
                Rating = 4.70,
                RatingCount = 780,
                DownloadsCount = 7800,
                CreatedAtUtc = new DateTime(2026, 2, 22, 16, 20, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 2, 22, 16, 20, 0, DateTimeKind.Utc),
                IsFeatured = false
            },

            // Space
            new CatalogItem
            {
                Id = Guid.Parse("30000000-0000-0000-0000-000000000001"),
                Title = "Cosmic Nebula Odyssey",
                Description = "Sublime deep space flight through swirling interstellar dust clouds and glowing proto-stars.",
                Author = "AstroLens",
                ThumbnailUrl = "assets/catalog/nebula_thumb.jpg",
                PreviewUrl = "assets/catalog/nebula_preview.mp4",
                Type = WallpaperType.Video,
                Width = 3840,
                Height = 2160,
                Duration = TimeSpan.FromSeconds(30),
                FileSizeBytes = 64 * 1024 * 1024,
                Tags = ["space", "nebula", "galaxy", "stars", "4k", "universe"],
                CategoryId = "space",
                CategoryName = "Space & Astronomy",
                Rating = 4.96,
                RatingCount = 2890,
                DownloadsCount = 28900,
                CreatedAtUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc),
                IsFeatured = true
            },
            new CatalogItem
            {
                Id = Guid.Parse("30000000-0000-0000-0000-000000000002"),
                Title = "Lunar Surface Horizon",
                Description = "Crisp detailed view of moon craters with planet Earth rising dramatically above the horizon.",
                Author = "AstroLens",
                ThumbnailUrl = "assets/catalog/lunar_thumb.jpg",
                PreviewUrl = "assets/catalog/lunar_preview.png",
                Type = WallpaperType.Image,
                Width = 3840,
                Height = 2160,
                FileSizeBytes = 6 * 1024 * 1024,
                Tags = ["moon", "earth", "space", "lunar", "4k", "black"],
                CategoryId = "space",
                CategoryName = "Space & Astronomy",
                Rating = 4.82,
                RatingCount = 1310,
                DownloadsCount = 13100,
                CreatedAtUtc = new DateTime(2026, 2, 8, 15, 30, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 2, 8, 15, 30, 0, DateTimeKind.Utc),
                IsFeatured = false
            },
            new CatalogItem
            {
                Id = Guid.Parse("30000000-0000-0000-0000-000000000003"),
                Title = "Solar Flare Corona",
                Description = "Hypnotic magnetic loops and intense plasma ejections on the sun's surface.",
                Author = "SolDynamics",
                ThumbnailUrl = "assets/catalog/solar_flare_thumb.jpg",
                PreviewUrl = "assets/catalog/solar_flare_preview.mp4",
                Type = WallpaperType.Video,
                Width = 1920,
                Height = 1080,
                Duration = TimeSpan.FromSeconds(12),
                FileSizeBytes = 19 * 1024 * 1024,
                Tags = ["sun", "plasma", "solar", "fire", "space", "orange"],
                CategoryId = "space",
                CategoryName = "Space & Astronomy",
                Rating = 4.65,
                RatingCount = 640,
                DownloadsCount = 6400,
                CreatedAtUtc = new DateTime(2026, 2, 25, 9, 10, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 2, 25, 9, 10, 0, DateTimeKind.Utc),
                IsFeatured = false
            },

            // Abstract
            new CatalogItem
            {
                Id = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                Title = "Prismatic Fluid Waves",
                Description = "Iridescent liquid gradients undulating in slow motion with subtle caustic light rays.",
                Author = "ChromaLab",
                ThumbnailUrl = "assets/catalog/fluid_waves_thumb.jpg",
                PreviewUrl = "assets/catalog/fluid_waves_preview.mp4",
                Type = WallpaperType.Video,
                Width = 3840,
                Height = 2160,
                Duration = TimeSpan.FromSeconds(20),
                FileSizeBytes = 45 * 1024 * 1024,
                Tags = ["abstract", "fluid", "iridescent", "waves", "4k", "colorful"],
                CategoryId = "abstract",
                CategoryName = "Abstract & Motion",
                Rating = 4.89,
                RatingCount = 1520,
                DownloadsCount = 15200,
                CreatedAtUtc = new DateTime(2026, 1, 28, 18, 0, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 1, 28, 18, 0, 0, DateTimeKind.Utc),
                IsFeatured = true
            },
            new CatalogItem
            {
                Id = Guid.Parse("40000000-0000-0000-0000-000000000002"),
                Title = "Minimalist Bauhaus Geometry",
                Description = "Harmonious primary shapes, clean lines, and soft paper textures inspired by modern Bauhaus design.",
                Author = "BauhausStudio",
                ThumbnailUrl = "assets/catalog/bauhaus_thumb.jpg",
                PreviewUrl = "assets/catalog/bauhaus_preview.png",
                Type = WallpaperType.Image,
                Width = 2560,
                Height = 1440,
                FileSizeBytes = 3 * 1024 * 1024,
                Tags = ["minimal", "bauhaus", "geometry", "abstract", "modern", "design"],
                CategoryId = "abstract",
                CategoryName = "Abstract & Motion",
                Rating = 4.75,
                RatingCount = 890,
                DownloadsCount = 8900,
                CreatedAtUtc = new DateTime(2026, 2, 14, 13, 40, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 2, 14, 13, 40, 0, DateTimeKind.Utc),
                IsFeatured = false
            },
            new CatalogItem
            {
                Id = Guid.Parse("40000000-0000-0000-0000-000000000003"),
                Title = "Liquid Chrome Symphony",
                Description = "Ultrawide reflective mercury ripples creating metallic distortion patterns across the screen.",
                Author = "ChromaLab",
                ThumbnailUrl = "assets/catalog/liquid_chrome_thumb.jpg",
                PreviewUrl = "assets/catalog/liquid_chrome_preview.mp4",
                Type = WallpaperType.Video,
                Width = 3440,
                Height = 1440,
                Duration = TimeSpan.FromSeconds(22),
                FileSizeBytes = 48 * 1024 * 1024,
                Tags = ["ultrawide", "chrome", "liquid", "metallic", "abstract", "silver"],
                CategoryId = "abstract",
                CategoryName = "Abstract & Motion",
                Rating = 4.81,
                RatingCount = 1040,
                DownloadsCount = 10400,
                CreatedAtUtc = new DateTime(2026, 2, 26, 21, 0, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 2, 26, 21, 0, 0, DateTimeKind.Utc),
                IsFeatured = false
            },

            // Gaming
            new CatalogItem
            {
                Id = Guid.Parse("50000000-0000-0000-0000-000000000001"),
                Title = "Retro 80s Arcade Glow",
                Description = "Nostalgic CRT scanlines with glowing arcade cabinets and pixel particle dust.",
                Author = "PixelHero",
                ThumbnailUrl = "assets/catalog/arcade_thumb.jpg",
                PreviewUrl = "assets/catalog/arcade_preview.mp4",
                Type = WallpaperType.Video,
                Width = 1920,
                Height = 1080,
                Duration = TimeSpan.FromSeconds(15),
                FileSizeBytes = 22 * 1024 * 1024,
                Tags = ["arcade", "retro", "pixel", "crt", "gaming", "neon"],
                CategoryId = "gaming",
                CategoryName = "Gaming & Retro",
                Rating = 4.80,
                RatingCount = 1230,
                DownloadsCount = 12300,
                CreatedAtUtc = new DateTime(2026, 1, 18, 16, 20, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 1, 18, 16, 20, 0, DateTimeKind.Utc),
                IsFeatured = false
            },
            new CatalogItem
            {
                Id = Guid.Parse("50000000-0000-0000-0000-000000000002"),
                Title = "Fantasy Dragon Citadel",
                Description = "Epic fortress built atop floating cliffs with an ancient guardian dragon resting under moonlight.",
                Author = "MythicArts",
                ThumbnailUrl = "assets/catalog/dragon_citadel_thumb.jpg",
                PreviewUrl = "assets/catalog/dragon_citadel_preview.png",
                Type = WallpaperType.Image,
                Width = 3840,
                Height = 2160,
                FileSizeBytes = 10 * 1024 * 1024,
                Tags = ["dragon", "fantasy", "castle", "epic", "4k", "gaming"],
                CategoryId = "gaming",
                CategoryName = "Gaming & Retro",
                Rating = 4.90,
                RatingCount = 1780,
                DownloadsCount = 17800,
                CreatedAtUtc = new DateTime(2026, 2, 4, 10, 15, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 2, 4, 10, 15, 0, DateTimeKind.Utc),
                IsFeatured = true
            },
            new CatalogItem
            {
                Id = Guid.Parse("50000000-0000-0000-0000-000000000003"),
                Title = "Mech Hangar Operations",
                Description = "Giant combat robot undergoing diagnostics with sparks flying and technician platforms moving.",
                Author = "SteelWorks",
                ThumbnailUrl = "assets/catalog/mech_hangar_thumb.jpg",
                PreviewUrl = "assets/catalog/mech_hangar_preview.mp4",
                Type = WallpaperType.Video,
                Width = 2560,
                Height = 1440,
                Duration = TimeSpan.FromSeconds(16),
                FileSizeBytes = 33 * 1024 * 1024,
                Tags = ["mech", "robot", "hangar", "sci-fi", "gaming", "sparks"],
                CategoryId = "gaming",
                CategoryName = "Gaming & Retro",
                Rating = 4.74,
                RatingCount = 910,
                DownloadsCount = 9100,
                CreatedAtUtc = new DateTime(2026, 2, 21, 14, 50, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 2, 21, 14, 50, 0, DateTimeKind.Utc),
                IsFeatured = false
            },

            // Anime
            new CatalogItem
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000001"),
                Title = "Cherry Blossom Wind Walk",
                Description = "Delicate sakura petals drifting softly across a sunlit riverside promenade in spring.",
                Author = "SakuraStudio",
                ThumbnailUrl = "assets/catalog/sakura_thumb.jpg",
                PreviewUrl = "assets/catalog/sakura_preview.mp4",
                Type = WallpaperType.Video,
                Width = 1920,
                Height = 1080,
                Duration = TimeSpan.FromSeconds(18),
                FileSizeBytes = 26 * 1024 * 1024,
                Tags = ["anime", "sakura", "cherry blossom", "spring", "lofi", "pink"],
                CategoryId = "anime",
                CategoryName = "Anime & Illustration",
                Rating = 4.93,
                RatingCount = 2150,
                DownloadsCount = 21500,
                CreatedAtUtc = new DateTime(2026, 1, 24, 11, 30, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 1, 24, 11, 30, 0, DateTimeKind.Utc),
                IsFeatured = true
            },
            new CatalogItem
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000002"),
                Title = "Floating Sky Archipelago",
                Description = "Whimsical anime illustration of grassy islands floating serenely among fluffy white clouds.",
                Author = "StudioGlow",
                ThumbnailUrl = "assets/catalog/floating_islands_thumb.jpg",
                PreviewUrl = "assets/catalog/floating_islands_preview.png",
                Type = WallpaperType.Image,
                Width = 3840,
                Height = 2160,
                FileSizeBytes = 8 * 1024 * 1024,
                Tags = ["anime", "clouds", "sky", "islands", "peaceful", "4k"],
                CategoryId = "anime",
                CategoryName = "Anime & Illustration",
                Rating = 4.85,
                RatingCount = 1470,
                DownloadsCount = 14700,
                CreatedAtUtc = new DateTime(2026, 2, 7, 16, 40, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 2, 7, 16, 40, 0, DateTimeKind.Utc),
                IsFeatured = false
            },
            new CatalogItem
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000003"),
                Title = "Night Train Through Clouds",
                Description = "A glowing train traveling across celestial starry tracks over a dreamy twilight cloud bank.",
                Author = "SakuraStudio",
                ThumbnailUrl = "assets/catalog/night_train_thumb.jpg",
                PreviewUrl = "assets/catalog/night_train_preview.mp4",
                Type = WallpaperType.Video,
                Width = 2560,
                Height = 1440,
                Duration = TimeSpan.FromSeconds(20),
                FileSizeBytes = 35 * 1024 * 1024,
                Tags = ["anime", "train", "stars", "night", "dream", "fantasy"],
                CategoryId = "anime",
                CategoryName = "Anime & Illustration",
                Rating = 4.88,
                RatingCount = 1620,
                DownloadsCount = 16200,
                CreatedAtUtc = new DateTime(2026, 2, 23, 20, 10, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 2, 23, 20, 10, 0, DateTimeKind.Utc),
                IsFeatured = false
            },

            // Minimal
            new CatalogItem
            {
                Id = Guid.Parse("70000000-0000-0000-0000-000000000001"),
                Title = "Monochrome Nordic Peaks",
                Description = "Ultra clean black and white high-contrast photographic study of snow ridges.",
                Author = "NordicCapture",
                ThumbnailUrl = "assets/catalog/nordic_peaks_thumb.jpg",
                PreviewUrl = "assets/catalog/nordic_peaks_preview.png",
                Type = WallpaperType.Image,
                Width = 3840,
                Height = 2160,
                FileSizeBytes = 4 * 1024 * 1024,
                Tags = ["minimal", "monochrome", "black and white", "mountains", "clean", "4k"],
                CategoryId = "minimal",
                CategoryName = "Minimal & Clean",
                Rating = 4.83,
                RatingCount = 1190,
                DownloadsCount = 11900,
                CreatedAtUtc = new DateTime(2026, 1, 30, 9, 0, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 1, 30, 9, 0, 0, DateTimeKind.Utc),
                IsFeatured = false
            },
            new CatalogItem
            {
                Id = Guid.Parse("70000000-0000-0000-0000-000000000002"),
                Title = "Desert Sand Dunes at Twilight",
                Description = "Smooth sweeping shadows across windswept desert dunes under pastel dusk skies.",
                Author = "SolitudeShots",
                ThumbnailUrl = "assets/catalog/sand_dunes_thumb.jpg",
                PreviewUrl = "assets/catalog/sand_dunes_preview.png",
                Type = WallpaperType.Image,
                Width = 2560,
                Height = 1440,
                FileSizeBytes = 5 * 1024 * 1024,
                Tags = ["desert", "minimal", "dunes", "twilight", "warm", "peaceful"],
                CategoryId = "minimal",
                CategoryName = "Minimal & Clean",
                Rating = 4.72,
                RatingCount = 740,
                DownloadsCount = 7400,
                CreatedAtUtc = new DateTime(2026, 2, 17, 18, 20, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 2, 17, 18, 20, 0, DateTimeKind.Utc),
                IsFeatured = false
            },

            // Cities
            new CatalogItem
            {
                Id = Guid.Parse("80000000-0000-0000-0000-000000000001"),
                Title = "Rainy London Twilight",
                Description = "Cinematic evening scene with red double-decker buses, wet cobblestone streets, and Big Ben.",
                Author = "UrbanMoments",
                ThumbnailUrl = "assets/catalog/london_rain_thumb.jpg",
                PreviewUrl = "assets/catalog/london_rain_preview.mp4",
                Type = WallpaperType.Video,
                Width = 3840,
                Height = 2160,
                Duration = TimeSpan.FromSeconds(24),
                FileSizeBytes = 51 * 1024 * 1024,
                Tags = ["london", "city", "rain", "twilight", "cinematic", "4k"],
                CategoryId = "cities",
                CategoryName = "Cities & Architecture",
                Rating = 4.93,
                RatingCount = 2040,
                DownloadsCount = 20400,
                CreatedAtUtc = new DateTime(2026, 1, 22, 17, 30, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 1, 22, 17, 30, 0, DateTimeKind.Utc),
                IsFeatured = true
            },
            new CatalogItem
            {
                Id = Guid.Parse("80000000-0000-0000-0000-000000000002"),
                Title = "Shibuya Crossing Time-Lapse",
                Description = "Dynamic pulse of the world's busiest pedestrian crossing under dazzling neon billboards.",
                Author = "UrbanMoments",
                ThumbnailUrl = "assets/catalog/shibuya_thumb.jpg",
                PreviewUrl = "assets/catalog/shibuya_preview.mp4",
                Type = WallpaperType.Video,
                Width = 1920,
                Height = 1080,
                Duration = TimeSpan.FromSeconds(15),
                FileSizeBytes = 24 * 1024 * 1024,
                Tags = ["shibuya", "tokyo", "city", "crossing", "timelapse", "night"],
                CategoryId = "cities",
                CategoryName = "Cities & Architecture",
                Rating = 4.86,
                RatingCount = 1580,
                DownloadsCount = 15800,
                CreatedAtUtc = new DateTime(2026, 2, 11, 21, 10, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 2, 11, 21, 10, 0, DateTimeKind.Utc),
                IsFeatured = false
            }
        ];
    }

    public Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<CatalogCategory>>(_categories);
    }

    public Task<IReadOnlyList<CatalogItem>> GetFeaturedAsync(int limit = 10, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var featured = _items
            .Where(x => x.IsFeatured)
            .OrderByDescending(x => x.Rating)
            .Take(Math.Max(1, limit))
            .ToList();

        return Task.FromResult<IReadOnlyList<CatalogItem>>(featured);
    }

    public Task<IReadOnlyList<CatalogItem>> GetPopularAsync(int limit = 10, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var popular = _items
            .OrderByDescending(x => x.DownloadsCount)
            .ThenByDescending(x => x.Rating)
            .Take(Math.Max(1, limit))
            .ToList();

        return Task.FromResult<IReadOnlyList<CatalogItem>>(popular);
    }

    public Task<IReadOnlyList<CatalogItem>> GetRecentAsync(int limit = 10, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var recent = _items
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Max(1, limit))
            .ToList();

        return Task.FromResult<IReadOnlyList<CatalogItem>>(recent);
    }

    public Task<CatalogItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var item = _items.FirstOrDefault(x => x.Id == id);
        return Task.FromResult(item);
    }

    public Task<CatalogPage<CatalogItem>> SearchAsync(CatalogSearchQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(query);

        int pageNumber = Math.Max(1, query.PageNumber);
        int pageSize = Math.Clamp(query.PageSize, 1, 100);

        IEnumerable<CatalogItem> results = _items;

        // 1. Filter criteria
        if (query.Filter != null)
        {
            var filter = query.Filter;

            if (!string.IsNullOrWhiteSpace(filter.CategoryId) &&
                !string.Equals(filter.CategoryId, "all", StringComparison.OrdinalIgnoreCase))
            {
                results = results.Where(x => string.Equals(x.CategoryId, filter.CategoryId, StringComparison.OrdinalIgnoreCase));
            }

            if (filter.Type.HasValue)
            {
                results = results.Where(x => x.Type == filter.Type.Value);
            }

            if (filter.Resolution != CatalogResolutionFilter.All)
            {
                results = results.Where(x => x.ResolutionCategory == filter.Resolution);
            }

            if (filter.Orientation != CatalogOrientationFilter.All)
            {
                results = results.Where(x => x.Orientation == filter.Orientation);
            }

            if (filter.MaxDuration.HasValue && filter.MaxDuration.Value > TimeSpan.Zero)
            {
                results = results.Where(x => x.Type != WallpaperType.Video || x.Duration <= filter.MaxDuration.Value);
            }

            if (filter.MinRating.HasValue)
            {
                results = results.Where(x => x.Rating >= filter.MinRating.Value);
            }

            if (filter.Tags != null && filter.Tags.Count > 0)
            {
                var targetTags = filter.Tags.Select(t => t.ToLowerInvariant()).ToHashSet();
                results = results.Where(x => x.Tags.Any(t => targetTags.Contains(t.ToLowerInvariant())));
            }
        }

        // 2. Text Search with tokenization and relevance scoring
        string rawTerm = query.SearchTerm?.Trim() ?? string.Empty;
        var searchTokens = Regex.Split(rawTerm.ToLowerInvariant(), @"\s+")
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();

        if (searchTokens.Length > 0)
        {
            results = results.Where(item => MatchesAllTokens(item, searchTokens));
        }

        // 3. Sorting
        results = query.SortBy switch
        {
            CatalogSortOption.Relevance => searchTokens.Length > 0
                ? results.OrderByDescending(item => CalculateRelevanceScore(item, rawTerm, searchTokens))
                         .ThenByDescending(item => item.DownloadsCount)
                : results.OrderByDescending(item => item.DownloadsCount).ThenByDescending(item => item.Rating),

            CatalogSortOption.Popular => results.OrderByDescending(item => item.DownloadsCount)
                                                .ThenByDescending(item => item.Rating),

            CatalogSortOption.Newest => results.OrderByDescending(item => item.CreatedAtUtc),

            CatalogSortOption.Rating => results.OrderByDescending(item => item.Rating)
                                               .ThenByDescending(item => item.RatingCount),

            CatalogSortOption.TitleAsc => results.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase),

            CatalogSortOption.TitleDesc => results.OrderByDescending(item => item.Title, StringComparer.OrdinalIgnoreCase),

            _ => results.OrderByDescending(item => item.DownloadsCount)
        };

        var evaluatedList = results.ToList();
        int totalCount = evaluatedList.Count;

        int skip = (pageNumber - 1) * pageSize;
        var pagedItems = evaluatedList.Skip(skip).Take(pageSize).ToList();

        var page = new CatalogPage<CatalogItem>(pagedItems, pageNumber, pageSize, totalCount);
        return Task.FromResult(page);
    }

    public Task<Stream> OpenDownloadStreamAsync(CatalogItem item, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(item);

        if (item.Type == XenMotion.Core.Enums.WallpaperType.Video)
            throw new NotSupportedException("This is a preview-only catalog entry. Import a real video from your computer or configure a remote catalog to download it.");

        string fileName = Path.GetFileName(item.ThumbnailUrl);
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Catalog", fileName);
        if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(path))
            throw new FileNotFoundException("The bundled wallpaper image is unavailable.", path);
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan));
    }
    private static bool MatchesAllTokens(CatalogItem item, string[] tokens)
    {
        string title = item.Title.ToLowerInvariant();
        string desc = item.Description.ToLowerInvariant();
        string author = item.Author.ToLowerInvariant();
        string category = item.CategoryName.ToLowerInvariant();
        var tags = item.Tags.Select(t => t.ToLowerInvariant()).ToList();

        foreach (string token in tokens)
        {
            bool matched = title.Contains(token) ||
                           desc.Contains(token) ||
                           author.Contains(token) ||
                           category.Contains(token) ||
                           tags.Any(t => t.Contains(token));

            if (!matched) return false;
        }

        return true;
    }

    private static int CalculateRelevanceScore(CatalogItem item, string rawQuery, string[] tokens)
    {
        int score = 0;
        string title = item.Title.ToLowerInvariant();
        string lowerQuery = rawQuery.ToLowerInvariant();

        if (title == lowerQuery) score += 100;
        else if (title.Contains(lowerQuery)) score += 50;

        foreach (var tag in item.Tags)
        {
            if (string.Equals(tag, lowerQuery, StringComparison.OrdinalIgnoreCase)) score += 40;
            else if (tokens.Any(t => tag.Contains(t, StringComparison.OrdinalIgnoreCase))) score += 20;
        }

        if (item.CategoryName.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase)) score += 25;
        if (item.Author.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase)) score += 20;
        if (item.Description.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase)) score += 10;

        return score;
    }
}
