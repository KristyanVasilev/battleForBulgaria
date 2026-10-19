namespace MapBuilder;

/// <summary>
/// The 28 provinces with a seed point (pixel inside the province on the reference image)
/// used to match colour regions to names. ISO codes follow ISO 3166-2:BG.
/// </summary>
public sealed record ProvinceSeed(string Id, string Iso, string Name, int SeedX, int SeedY);

public static class ProvinceCatalog
{
    public static readonly ProvinceSeed[] All =
    [
        new("blagoevgrad", "BG-01", "Благоевград", 120, 455),
        new("burgas", "BG-02", "Бургас", 660, 330),
        new("varna", "BG-03", "Варна", 655, 270),
        new("veliko-tarnovo", "BG-04", "Велико Търново", 385, 172),
        new("vidin", "BG-05", "Видин", 40, 120),
        new("vratsa", "BG-06", "Враца", 185, 170),
        new("gabrovo", "BG-07", "Габрово", 350, 265),
        new("dobrich", "BG-08", "Добрич", 760, 145),
        new("kardzhali", "BG-09", "Кърджали", 380, 495),
        new("kyustendil", "BG-10", "Кюстендил", 50, 375),
        new("lovech", "BG-11", "Ловеч", 290, 250),
        new("montana", "BG-12", "Монтана", 130, 165),
        new("pazardzhik", "BG-13", "Пазарджик", 230, 380),
        new("pernik", "BG-14", "Перник", 50, 320),
        new("pleven", "BG-15", "Плевен", 300, 210),
        new("plovdiv", "BG-16", "Пловдив", 310, 360),
        new("razgrad", "BG-17", "Разград", 570, 180),
        new("ruse", "BG-18", "Русе", 450, 180),
        new("silistra", "BG-19", "Силистра", 570, 105),
        new("sliven", "BG-20", "Сливен", 500, 300),
        new("smolyan", "BG-21", "Смолян", 290, 515),
        new("sofia-city", "BG-22", "София (град)", 135, 340),
        new("sofia-province", "BG-23", "София (област)", 220, 290),
        new("stara-zagora", "BG-24", "Стара Загора", 380, 330),
        new("targovishte", "BG-25", "Търговище", 510, 255),
        new("haskovo", "BG-26", "Хасково", 490, 440),
        new("shumen", "BG-27", "Шумен", 590, 260),
        new("yambol", "BG-28", "Ямбол", 540, 410),
    ];
}
