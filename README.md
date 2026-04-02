# ErenshorRU — Русификатор Erenshor

Мод для полной (В настоящее время не обеспечивается 100% покрытие фраз\текста) русификации [Erenshor](https://store.steampowered.com/app/2382520/Erenshor/) — однопользовательской RPG в стиле классических MMORPG.

## Что переводится

- Интерфейс, меню, настройки
- Описания классов, статов, навыков и заклинаний
- Квесты и диалоги
- Предметы и лор
- Чат SimPlayer'ов и системные сообщения

Файлы переводов лежат в папке `Translation/` в формате `английский=русский` — легко править и дополнять.

## Установка

1. Установить [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) в папку игры
2. Запустить игру один раз, чтобы BepInEx создал структуру папок
3. Скопировать папку `ErenshorRU` целиком в `<папка игры>/BepInEx/plugins/`
4. Запустить игру

Структура должна выглядеть так:

```
Erenshor/
└── BepInEx/
    └── plugins/
        └── ErenshorRU/
            ├── ErenshorRU.dll
            └── Translation/
                ├── ui_system.txt
                ├── ui_extra.txt
                ├── misc.txt
                ├── quests.txt
                ├── items_lore.txt
                ├── skills_spells.txt
                └── simplayer_chat.txt
```

## Сборка из исходников

Требуется .NET SDK и установленная игра.

1. Открыть `ErenshorRU.csproj`
2. Поправить путь `GameDir`, если игра стоит не в стандартной папке Steam
3. `dotnet build -c Release`

## Как работает

Мод перехватывает сеттеры `TMP_Text.text` и `Text.text` через Harmony-патчи и подставляет русский перевод на лету. Кириллица отображается через системный шрифт Segoe UI, подключаемый как fallback.

## Лицензия

[MIT](LICENSE)
