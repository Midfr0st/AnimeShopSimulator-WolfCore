# Установка WolfCore

[Вернуться на главную страницу](../README.md)

## Требования

- Anime Shop Simulator для Windows;
- [MelonLoader](https://github.com/LavaGang/MelonLoader) `0.7.3`;
- `WolfCore.dll` из выпуска, совместимого с вашей версией игры.

## 1. Найдите папку игры

В Steam откройте:

```text
Библиотека → Anime Shop Simulator → Управление → Просмотреть локальные файлы
```

## 2. Установите MelonLoader

1. Скачайте MelonLoader из [официального репозитория](https://github.com/LavaGang/MelonLoader).
2. Укажите исполняемый файл Anime Shop Simulator при установке.
3. Запустите игру один раз и дождитесь главного меню.
4. Закройте игру.

После первого запуска должны появиться каталоги `MelonLoader`, `Mods` и `UserData`.

## 3. Установите WolfCore

1. Откройте [последний выпуск WolfCore](https://github.com/Midfr0st/AnimeShopSimulator-WolfCore/releases/latest).
2. Скачайте `WolfCore.dll` или архив `WolfCore-0.2.5.zip`.
3. Скопируйте `WolfCore.dll` непосредственно в папку `Anime Shop Simulator\Mods`.

```text
Anime Shop Simulator\Mods\WolfCore.dll
```

Не помещайте в `Mods` ZIP-архив, исходники или `.csproj`.

## 4. Проверьте установку

1. Запустите игру.
2. Нажмите `Esc`.
3. В меню паузы должна появиться кнопка `Моды`.

Внутри будут видны WolfCore и все совместимые установленные модули.

## Обновление

1. Полностью закройте игру.
2. Скачайте новую DLL из Releases.
3. Замените старый файл в `Mods`.
4. Удалите возможные копии вроде `WolfCore (1).dll`.
5. Запустите игру.

Настройки в `UserData\WolfCore.settings.json` сохраняются при обычном обновлении.

## Удаление

1. Закройте игру.
2. Удалите `Mods\WolfCore.dll`.
3. Удалите зависимые WolfMods либо учитывайте, что без ядра они не запустятся.

Чтобы сбросить настройки ядра, удалите `UserData\WolfCore.settings.json`. Игровое сохранение это не затрагивает.
