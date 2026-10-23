using System.Text.Json;
using Serilog;
using SkinForge.Core.Interfaces;

namespace SkinForge.Application.Services;

public class LocalizationService : ILocalizationService
{
    private readonly Dictionary<string, Dictionary<string, string>> _translations = new(StringComparer.OrdinalIgnoreCase);
    private string _currentLanguage = "en";

    public string CurrentLanguage => _currentLanguage;
    public IReadOnlyList<string> SupportedLanguages => ["en", "tr", "de", "fr", "es", "pt", "ja", "ko", "zh", "zh-tw", "ru", "pl", "it", "vi"];

    public event EventHandler? LanguageChanged;

    public LocalizationService(string resourcesDirectory)
    {
        LoadTranslations(resourcesDirectory);
    }

    private void LoadTranslations(string resourcesDirectory)
    {
        foreach (var lang in SupportedLanguages)
        {
            var filePath = Path.Combine(resourcesDirectory, $"Strings.{lang}.json");
            if (File.Exists(filePath))
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
                    _translations[lang] = new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
                    Log.Information("Loaded {Count} localization strings for language '{Lang}'.", dict.Count, lang);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to load localization file '{Path}'.", filePath);
                }
            }
            else
            {
                // Fallback default built-ins
                _translations[lang] = GetBuiltinDefaults(lang);
            }
        }
    }

    public string GetString(string key)
    {
        if (_translations.TryGetValue(_currentLanguage, out var currentDict) && currentDict.TryGetValue(key, out var val))
        {
            return val;
        }

        // Fallback to English
        if (_translations.TryGetValue("en", out var enDict) && enDict.TryGetValue(key, out var enVal))
        {
            return enVal;
        }

        return key;
    }

    public void SetLanguage(string languageCode)
    {
        if (SupportedLanguages.Contains(languageCode, StringComparer.OrdinalIgnoreCase) &&
            !string.Equals(_currentLanguage, languageCode, StringComparison.OrdinalIgnoreCase))
        {
            _currentLanguage = languageCode.ToLowerInvariant();
            Log.Information("Active language changed to '{Lang}'.", _currentLanguage);
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static Dictionary<string, string> GetBuiltinDefaults(string lang)
    {
        return lang.ToLowerInvariant() switch
        {
            "tr" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "SkinForge",
                ["SearchPlaceholder"] = "Kostüm, şampiyon veya mod ara...",
                ["InstalledSkins"] = "Yüklü Kostümler",
                ["ActiveMods"] = "Aktif Modlar",
                ["Champions"] = "Şampiyonlar",
                ["StorageUsed"] = "Kullanılan Alan",
                ["ImportSkin"] = "Kostüm İçe Aktar",
                ["NoSkinsInstalled"] = "Henüz hiçbir özel kostüm yüklenmedi.",
                ["ImportFirstSkin"] = "İlk kostümünüzü içe aktarın",
                ["AllChampions"] = "Tüm Şampiyonlar",
                ["AllCategories"] = "Tüm Kategoriler",
                ["SortByName"] = "İsme Göre",
                ["ConflictDetected"] = "Mod Çakışması Tespit Edildi",
                ["Settings"] = "Ayarlar",
                ["DeactivateAll"] = "Hepsini Devre Dışı Bırak",
                ["RestoreClean"] = "Temiz Durumu Geri Yükle"
            },
            "de" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "SkinForge",
                ["SearchPlaceholder"] = "Skins, Champions oder Mods suchen...",
                ["InstalledSkins"] = "Installierte Skins",
                ["ActiveMods"] = "Aktive Mods",
                ["Champions"] = "Champions",
                ["StorageUsed"] = "Belegter Speicher",
                ["ImportSkin"] = "Skin importieren",
                ["NoSkinsInstalled"] = "Noch keine Custom Skins installiert.",
                ["ImportFirstSkin"] = "Importieren Sie Ihren ersten Skin",
                ["AllChampions"] = "Alle Champions",
                ["AllCategories"] = "Alle Kategorien",
                ["SortByName"] = "Nach Name",
                ["ConflictDetected"] = "Mod-Konflikt erkannt",
                ["Settings"] = "Einstellungen",
                ["DeactivateAll"] = "Alle deaktivieren",
                ["RestoreClean"] = "Sauberen Zustand wiederherstellen"
            },
            "fr" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "SkinForge",
                ["SearchPlaceholder"] = "Rechercher des skins, champions ou mods...",
                ["InstalledSkins"] = "Skins installés",
                ["ActiveMods"] = "Mods actifs",
                ["Champions"] = "Champions",
                ["StorageUsed"] = "Espace utilisé",
                ["ImportSkin"] = "Importer un skin",
                ["NoSkinsInstalled"] = "Aucun skin personnalisé installé pour le moment.",
                ["ImportFirstSkin"] = "Importez votre premier skin",
                ["AllChampions"] = "Tous les champions",
                ["AllCategories"] = "Toutes les catégories",
                ["SortByName"] = "Par nom",
                ["ConflictDetected"] = "Conflit de mod détecté",
                ["Settings"] = "Paramètres",
                ["DeactivateAll"] = "Tout désactiver",
                ["RestoreClean"] = "Restaurer l'état propre"
            },
            "es" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "SkinForge",
                ["SearchPlaceholder"] = "Buscar aspectos, campeones o mods...",
                ["InstalledSkins"] = "Aspectos instalados",
                ["ActiveMods"] = "Mods activos",
                ["Champions"] = "Campeones",
                ["StorageUsed"] = "Almacenamiento usado",
                ["ImportSkin"] = "Importar aspecto",
                ["NoSkinsInstalled"] = "Aún no hay aspectos personalizados instalados.",
                ["ImportFirstSkin"] = "Importa tu primer aspecto",
                ["AllChampions"] = "Todos los campeones",
                ["AllCategories"] = "Todas las categorías",
                ["SortByName"] = "Por nombre",
                ["ConflictDetected"] = "Conflicto de mod detectado",
                ["Settings"] = "Ajustes",
                ["DeactivateAll"] = "Desactivar todos",
                ["RestoreClean"] = "Restaurar estado limpio"
            },
            "pt" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "SkinForge",
                ["SearchPlaceholder"] = "Pesquisar skins, campeões ou mods...",
                ["InstalledSkins"] = "Skins instaladas",
                ["ActiveMods"] = "Mods ativos",
                ["Champions"] = "Campeões",
                ["StorageUsed"] = "Espaço usado",
                ["ImportSkin"] = "Importar skin",
                ["NoSkinsInstalled"] = "Nenhuma skin personalizada instalada ainda.",
                ["ImportFirstSkin"] = "Importe sua primeira skin",
                ["AllChampions"] = "Todos os campeões",
                ["AllCategories"] = "Todas as categorias",
                ["SortByName"] = "Por nome",
                ["ConflictDetected"] = "Conflito de mod detectado",
                ["Settings"] = "Configurações",
                ["DeactivateAll"] = "Desativar tudo",
                ["RestoreClean"] = "Restaurar estado limpo"
            },
            "ja" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "SkinForge",
                ["SearchPlaceholder"] = "スキン、チャンピオン、MODを検索...",
                ["InstalledSkins"] = "インストール済みスキン",
                ["ActiveMods"] = "有効なMOD",
                ["Champions"] = "チャンピオン",
                ["StorageUsed"] = "使用ストレージ",
                ["ImportSkin"] = "スキンをインポート",
                ["NoSkinsInstalled"] = "カスタムスキンはまだインストールされていません。",
                ["ImportFirstSkin"] = "最初のスキンをインポートする",
                ["AllChampions"] = "すべてのチャンピオン",
                ["AllCategories"] = "すべてのカテゴリー",
                ["SortByName"] = "名前順",
                ["ConflictDetected"] = "MODの競合を検出",
                ["Settings"] = "設定",
                ["DeactivateAll"] = "すべて無効化",
                ["RestoreClean"] = "クリーン状態に復元"
            },
            "ko" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "SkinForge",
                ["SearchPlaceholder"] = "스킨, 챔피언, 모드 검색...",
                ["InstalledSkins"] = "설치된 스킨",
                ["ActiveMods"] = "활성 모드",
                ["Champions"] = "챔피언",
                ["StorageUsed"] = "사용 용량",
                ["ImportSkin"] = "스킨 가져오기",
                ["NoSkinsInstalled"] = "아직 설치된 커스텀 스킨이 없습니다.",
                ["ImportFirstSkin"] = "첫 번째 스킨을 가져오세요",
                ["AllChampions"] = "모든 챔피언",
                ["AllCategories"] = "모든 카테고리",
                ["SortByName"] = "이름순",
                ["ConflictDetected"] = "모드 충돌 감지됨",
                ["Settings"] = "설정",
                ["DeactivateAll"] = "모두 비활성화",
                ["RestoreClean"] = "클린 상태로 복구"
            },
            "zh" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "SkinForge",
                ["SearchPlaceholder"] = "搜索皮肤、英雄或模组...",
                ["InstalledSkins"] = "已安装皮肤",
                ["ActiveMods"] = "活动模组",
                ["Champions"] = "英雄",
                ["StorageUsed"] = "已用空间",
                ["ImportSkin"] = "导入皮肤",
                ["NoSkinsInstalled"] = "尚未安装任何自定义皮肤。",
                ["ImportFirstSkin"] = "导入您的首个皮肤",
                ["AllChampions"] = "所有英雄",
                ["AllCategories"] = "所有分类",
                ["SortByName"] = "按名称",
                ["ConflictDetected"] = "检测到模组冲突",
                ["Settings"] = "设置",
                ["DeactivateAll"] = "全部停用",
                ["RestoreClean"] = "恢复纯净状态"
            },
            "ru" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "SkinForge",
                ["SearchPlaceholder"] = "Поиск скинов, чемпионов или модов...",
                ["InstalledSkins"] = "Установленные скины",
                ["ActiveMods"] = "Активные моды",
                ["Champions"] = "Чемпионы",
                ["StorageUsed"] = "Занятое место",
                ["ImportSkin"] = "Импортировать скин",
                ["NoSkinsInstalled"] = "Пользовательские скины еще не установлены.",
                ["ImportFirstSkin"] = "Импортируйте свой первый скин",
                ["AllChampions"] = "Все чемпионы",
                ["AllCategories"] = "Все категории",
                ["SortByName"] = "По имени",
                ["ConflictDetected"] = "Обнаружен конфликт модов",
                ["Settings"] = "Настройки",
                ["DeactivateAll"] = "Отключить все",
                ["RestoreClean"] = "Восстановить чистый клиент"
            },
            "zh-tw" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "SkinForge",
                ["SearchPlaceholder"] = "搜尋造型、英雄或模組...",
                ["InstalledSkins"] = "已安裝造型",
                ["ActiveMods"] = "啟用中的模組",
                ["Champions"] = "英雄",
                ["StorageUsed"] = "已用空間",
                ["ImportSkin"] = "匯入造型",
                ["NoSkinsInstalled"] = "尚未安裝任何自訂造型。",
                ["ImportFirstSkin"] = "匯入您的第一個造型",
                ["AllChampions"] = "所有英雄",
                ["AllCategories"] = "所有分類",
                ["SortByName"] = "按名稱",
                ["ConflictDetected"] = "偵測到模組衝突",
                ["Settings"] = "設定",
                ["DeactivateAll"] = "全部停用",
                ["RestoreClean"] = "還原純淨狀態"
            },
            "pl" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "SkinForge",
                ["SearchPlaceholder"] = "Szukaj skórek, bohaterów lub modów...",
                ["InstalledSkins"] = "Zainstalowane skórki",
                ["ActiveMods"] = "Aktywne mody",
                ["Champions"] = "Bohaterowie",
                ["StorageUsed"] = "Zajęte miejsce",
                ["ImportSkin"] = "Importuj skórkę",
                ["NoSkinsInstalled"] = "Brak zainstalowanych niestandardowych skórek.",
                ["ImportFirstSkin"] = "Zaimportuj swoją pierwszą skórkę",
                ["AllChampions"] = "Wszyscy bohaterowie",
                ["AllCategories"] = "Wszystkie kategorie",
                ["SortByName"] = "Według nazwy",
                ["ConflictDetected"] = "Wykryto konflikt modów",
                ["Settings"] = "Ustawienia",
                ["DeactivateAll"] = "Wyłącz wszystkie",
                ["RestoreClean"] = "Przywróć stan czysty"
            },
            "it" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "SkinForge",
                ["SearchPlaceholder"] = "Cerca skin, campioni o mod...",
                ["InstalledSkins"] = "Skin installate",
                ["ActiveMods"] = "Mod attive",
                ["Champions"] = "Campioni",
                ["StorageUsed"] = "Spazio utilizzato",
                ["ImportSkin"] = "Importa skin",
                ["NoSkinsInstalled"] = "Nessuna skin personalizzata installata.",
                ["ImportFirstSkin"] = "Importa la tua prima skin",
                ["AllChampions"] = "Tutti i campioni",
                ["AllCategories"] = "Tutte le categorie",
                ["SortByName"] = "Per nome",
                ["ConflictDetected"] = "Rilevato conflitto mod",
                ["Settings"] = "Impostazioni",
                ["DeactivateAll"] = "Disattiva tutto",
                ["RestoreClean"] = "Ripristina stato pulito"
            },
            "vi" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "SkinForge",
                ["SearchPlaceholder"] = "Tìm trang phục, tướng hoặc mod...",
                ["InstalledSkins"] = "Trang phục đã cài đặt",
                ["ActiveMods"] = "Mod đang hoạt động",
                ["Champions"] = "Tướng",
                ["StorageUsed"] = "Dung lượng đã dùng",
                ["ImportSkin"] = "Nhập trang phục",
                ["NoSkinsInstalled"] = "Chưa có trang phục tùy chỉnh nào được cài đặt.",
                ["ImportFirstSkin"] = "Nhập trang phục đầu tiên của bạn",
                ["AllChampions"] = "Tất cả tướng",
                ["AllCategories"] = "Tất cả danh mục",
                ["SortByName"] = "Theo tên",
                ["ConflictDetected"] = "Phát hiện xung đột mod",
                ["Settings"] = "Cài đặt",
                ["DeactivateAll"] = "Hủy kích hoạt tất cả",
                ["RestoreClean"] = "Khôi phục trạng thái sạch"
            },
            _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "SkinForge",
                ["SearchPlaceholder"] = "Search skins, champions or mods...",
                ["InstalledSkins"] = "Installed Skins",
                ["ActiveMods"] = "Active Mods",
                ["Champions"] = "Champions",
                ["StorageUsed"] = "Storage Used",
                ["ImportSkin"] = "Import Skin",
                ["NoSkinsInstalled"] = "No custom skins installed yet.",
                ["ImportFirstSkin"] = "Import your first skin",
                ["AllChampions"] = "All Champions",
                ["AllCategories"] = "All Categories",
                ["SortByName"] = "By Name",
                ["ConflictDetected"] = "Mod Conflict Detected",
                ["Settings"] = "Settings",
                ["DeactivateAll"] = "Deactivate All",
                ["RestoreClean"] = "Restore Clean State"
            }
        };
    }
}
