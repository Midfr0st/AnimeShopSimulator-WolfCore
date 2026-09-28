# WolfCore — Anime Shop Simulator

![Anime Shop Simulator](https://img.shields.io/badge/Anime%20Shop%20Simulator-1.0.6-f6a800)
![WolfCore](https://img.shields.io/badge/WolfCore-0.2.5-1685d1)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3-7952b3)
![Platform](https://img.shields.io/badge/Windows-x64-2672ec)
![License](https://img.shields.io/badge/license-MIT-2ea44f)

**WolfCore** — обязательное ядро и единое меню настроек для модов WolfMods в **Anime Shop Simulator**.

Само ядро не меняет баланс, товары или игровые сохранения. Оно добавляет общую панель `Esc` → `Моды`, хранит настройки установленных модулей и подключает их страницы к игровому терминалу.

## Скачать

Готовая сборка находится в разделе **[Releases](https://github.com/Midfr0st/AnimeShopSimulator-WolfCore/releases/latest)**.

Для обычной установки нужен только `WolfCore.dll`. Исходный код скачивать и собирать самостоятельно не требуется.

## Возможности

- единая кнопка `Моды` в меню паузы;
- список установленных совместимых модов, их версий и описаний;
- включение, выключение и настройка модулей из одного меню;
- общий реестр дополнительных страниц игрового терминала;
- автоматические дополнительные экраны терминала, если плиток становится больше шести;
- стабильный порядок плиток независимо от порядка загрузки DLL;
- сохранение настроек WolfMods без изменения игровых сохранений.

## Быстрая установка

1. Полностью закройте игру.
2. Установите [MelonLoader](https://github.com/LavaGang/MelonLoader) в папку Anime Shop Simulator.
3. Один раз запустите и закройте игру, чтобы появились служебные папки.
4. Скачайте `WolfCore.dll` из [последнего выпуска](https://github.com/Midfr0st/AnimeShopSimulator-WolfCore/releases/latest).
5. Поместите файл в:

   ```text
   Anime Shop Simulator\Mods\WolfCore.dll
   ```

6. Запустите игру и откройте `Esc` → `Моды`.

Подробная инструкция: **[установка, обновление и удаление](docs/INSTALLATION.ru.md)**.

## Моды на базе WolfCore

Все модули независимы друг от друга, но требуют установленный WolfCore.

| Мод | Назначение |
| --- | --- |
| [Фильтры полок](https://github.com/Midfr0st/AnimeShopSimulator-ShelfFilters) | Правила автоматической выкладки для отдельных секций мебели |
| [Статистика товаров](https://github.com/Midfr0st/AnimeShopSimulator-ProductStatistics) | Продажи, выручка и расчётная прибыль по каждому товару |
| [Отзывы о магазине](https://github.com/Midfr0st/AnimeShopSimulator-ShopReviews) | Рейтинг и отзывы покупателей на основе игровых событий |
| [Расписание работников](https://github.com/Midfr0st/AnimeShopSimulator-EmployeeSchedules) | Работа выбранных профессий до открытия и после закрытия |

## Совместимость

| Компонент | Поддерживаемая версия |
| --- | --- |
| Anime Shop Simulator | `1.0.6` |
| MelonLoader | `0.7.3` |
| Платформа | Windows x64 |
| Сборка игры | Unity IL2CPP |
| WolfCore | `0.2.5` |

После обновлений игры совместимость может потребовать повторной проверки.

## Где хранятся настройки

```text
Anime Shop Simulator\UserData\WolfCore.settings.json
```

Удаление этого файла сбросит настройки WolfCore, но не затронет игровое сохранение.

## Если что-то не работает

Сначала проверьте, что:

- `WolfCore.dll` лежит непосредственно в папке `Mods`;
- MelonLoader запускается вместе с игрой;
- в `Mods` нет копий WolfCore с другими именами;
- версия игры соответствует таблице совместимости.

Подробности: **[решение проблем](docs/TROUBLESHOOTING.ru.md)**.

Для отчёта об ошибке приложите `Anime Shop Simulator\MelonLoader\Latest.log` и создайте обращение в [GitHub Issues](https://github.com/Midfr0st/AnimeShopSimulator-WolfCore/issues).

## Для разработчиков

- описание интеграции: [API WolfCore](docs/API.ru.md);
- сборка из исходников: [BUILDING.ru.md](docs/BUILDING.ru.md).

Игровые библиотеки и файлы MelonLoader в репозитории не распространяются. При сборке они берутся из локальной установки игры.

## Лицензия и отказ от ответственности

Это неофициальная пользовательская модификация, не связанная с разработчиками или издателем Anime Shop Simulator. Название игры и игровые материалы принадлежат их правообладателям.

Проект распространяется по условиям [MIT License](LICENSE). Мод предоставляется «как есть» и используется на свой риск. Изменённые и повторно опубликованные третьими лицами сборки не являются официальными.
