> [!CAUTION]
> Это моя первая работа, я еще не знаю всех тонкостей, могут быть баги, не исключаю критических!
# Об плагине / About Plugin
MatchMake - одно из решений для проведения турниров или так называемых миксов. Плагин не дает заходить другим игрокам за команды, если уже идет матч. Матч проходит по следующему сценарию Разминка/Ножевой/Выбор стороны/Основной матч/Конец. Если вы нашли какие-то недочеты/баги просьба сообщить в дисокрд - phoenixcs2\
\
MatchMake is a solution for hosting tournaments or so-called mix matches. The plugin prevents other players from joining the teams once a match has started. The match follows this scenario: Warmup/Knife Round/Side Selection/Main Match/End. If you find any issues/bugs, please report them on Discord - phoenixcs2
## Требования / Requirements
- [Metamod](https://www.sourcemm.net/downloads.php/?branch=master)
- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)
- gamemode_competitive.cfg
## Установка / Install
Скачайте последнюю версию плагина, достаточно просто перекинуть папку `addons` в `game/csgo/`\
Вы можете отредактировать переводы в папке `plugins/MatchMake/lang/`\
\
Download the latest version of the plugin, simply move the `addons` folder to `game/csgo/`\
You can edit the translations in the `plugins/MatchMake/lang/` folder.
## Конфиг / Config
- `PlayersForStart` - Сколько игроков нужно, чтобы игроки могли начать матч.
- `EnableMessageAboutDamage` - Сообщение в конце раунда о нанесенном уроне, а также о полученном. `true` показывается, `false` - не выводиться.
- `CanDraw` - Можно ли допускать ничью, `true` - Игроки смогут сыграть в ничью, `false` - ничьи не будет, будут играться овертаймы до победителя.
- `TypeFriendlyFire` - Тип урона по союзникам, `Off` - Не будет урона по союзникам, `Faceit` - Только от HE/Molotov, `All` - Полностью урон проходит.
- `CountPauses` - Кол-во пауз, который сможет взять лидер за всю игру.
- `PauseTime` - Сколько секунд длится пауза.
- `EnableAutoStart` - Будет ли матч запускаться сразу при заходе игроков, `true` - Матч будет сам запускаться, `false` - только `@css/root` сможет запустить матч.
- `AutoReplacePlayers` - Когда выходит из игры какой-нибудь игрок который был в игре, переносить ли наблюдателя на его место, `true` - будет моментальная замена (при наличии спектатора), `false` - не будет никакой замены.\
  🏴󠁧󠁢󠁥󠁮󠁧󠁿:
- `PlayersForStart` - The number of players required for the match to be ready to start.
- `EnableMessageAboutDamage` - Display a message at the end of the round showing damage dealt and received. `true` = enabled, `false` = disabled.
- `CanDraw` - Allow the match to end in a tie. `true` = a draw is possible, `false` = no draws, overtime will be played until a winner is decided.
- `TypeFriendlyFire` - Friendly fire (team damage) type. `Off` = No friendly fire. `Faceit` = Only HE/Molotov damage to teammates. `All` = Full friendly fire damage.
- `CountPauses` - The number of pauses a team leader can call during the entire match.
- `PauseTime` - The duration of a pause in seconds.
- `EnableAutoStart` - Automatically start the match once all players have connected. `true` = Match starts automatically, `false` = Only a player with `@css/root` flag can start the match.
- `AutoReplacePlayers` - Automatically replace a disconnected player with a spectator. `true` = Instant replacement (if a spectator is available), `false` = No replacement.
## Команды / Commands
- `!ready/!r` - Подтверждение готовности игрока.
- `!unready/!ur` - Убирает готовность игрока.
- `!stay/!switch` - Победитель определяет остаться или поменять сторону [Доступно только лидерам!]
- `!pause/!unpause` - Поставить паузу/Убрать паузу [Доступно только лидерам!]
- `!changename` - Поменять название своей команде [Доступно только лидерам!]
- `css_startmatch` - Запускает матч [Доступно только для @css/root]
- `css_restartmatch` - Перезапускает, если матч уже идет [Доступно только для @css/root]
- `css_addleader <steamid64>` - Добавляет лидера [Доступно только для @css/root]
- `css_delleader <steamdid64>` - Убирает лидера [Доступно только для @css/root]
- `css_mm_reload` - Обновляет данные из конфига [Доступно только для @css/root]\
🏴󠁧󠁢󠁥󠁮󠁧󠁿:
- `!ready/!r` - Confirm player readiness.
- `!unready/!ur` - Cancel player readiness.
- `!stay/!switch` - Winner decides to stay or switch sides [Leaders only!]
- `!pause/!unpause` - Start a pause / End a pause [Leaders only!]
- `!changename` - Change your team name [Leaders only!]
- `css_startmatch` - Start the match [@css/root only]
- `css_restartmatch` - Restart the match if already in progress [@css/root only]
- `css_addleader <steamid64>` - Add a team leader [@css/root only]
- `css_delleader <steamid64>` - Remove a team leader [@css/root only]
- `css_mm_reload` - Reload configuration data [@css/root only]
