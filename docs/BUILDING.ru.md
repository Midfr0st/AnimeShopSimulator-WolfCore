# Сборка из исходников

## Требования

- .NET 6 SDK;
- установленная Anime Shop Simulator;
- установленный в игру MelonLoader.

## Команда

Из корня репозитория:

```powershell
dotnet build WolfCore.csproj -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Anime Shop Simulator"
```

Вместо параметра можно задать переменную окружения `ANIME_SHOP_GAME_DIR`.

Результат сборки находится в `bin\Release\net6.0\WolfCore.dll`.
