# Efiron Presentation Refactor Decision

**Статус:** APPROVED FOR IMPLEMENTATION  
**Дата:** 2026-07-29  
**Связанные элементы:** Issue #15, Draft PR #16  
**Основание:** утверждённый постер `EFIRON UI/UX КОНЦЕПЦИЯ v0.1`, утверждённый Implementation Mapping v1 и Windows runtime-запись Phase 2A с аудиокомментарием

---

## 1. Решение

Phase 2A с динамическим переносом старых WinUI-контролов между существующими панелями отклонён.

Дальнейшая реализация выполняется как **полный рефакторинг presentation-слоя Efiron**, а не как перестановка, перекраска или постепенное расширение старого `MainWindow.xaml`.

Это не означает бессистемное переписывание всего приложения. Проверенная доменная и runtime-инфраструктура сохраняется и подключается к новой presentation-архитектуре через явные сервисы состояния и команды.

---

## 2. Runtime-факты, вызвавшие решение

Windows-запись Phase 2A подтвердила:

1. от запуска процесса до появления пригодного интерфейса проходит около 30 секунд;
2. до загрузки интерфейса пользователь видит чёрное окно/ожидание;
3. вложенная навигация Настроек в основном открывает пустые описательные разделы;
4. интерфейс остаётся визуально и композиционно близким к старой технической форме;
5. физический reparenting старых контролов не формирует архитектуру утверждённого продукта;
6. продолжение такого подхода увеличит стоимость последующей полной замены.

Phase 2A не является baseline и удалён из ветки.

---

## 3. Граница рефакторинга

## 3.1. Полностью перестраивается

- окно и оболочка приложения;
- главная навигация и host текущего раздела;
- экран `Эфир` в Wide, Medium и Compact;
- EPG `Канал` и `Сетка`;
- экран `Каналы` и постоянный редактор;
- экран `Настройки` и его внутренние разделы;
- общие строки каналов и передач;
- категории, поиск, badges, focus, selected и playing states;
- presentation-state, команды и orchestration запуска;
- empty/loading/error/welcome states;
- адаптация к клавиатуре и пульту.

## 3.2. Сохраняется

- `M3uPlaylistParser`;
- `RemotePlaylistClient`;
- `XmlTvParser`;
- `RemoteEpgClient`;
- `EpgChannelMatcher`;
- `EpgScheduleIndex`;
- Core-модель каталога каналов;
- нумерация, избранное, скрытие и пользовательские overrides;
- JSON-хранилища канального каталога и оформления;
- LibVLCSharp и проверенная логика воспроизведения;
- fullscreen lifecycle;
- принятые правила fit-to-width EPG;
- диапазоны до 50 каналов и фильтрация до формирования диапазонов;
- Core-тесты и CI-барьеры.

## 3.3. Временно допустимо

Скрытые compatibility bridges допускаются только для поэтапного подключения проверенной логики, если они:

- невидимы пользователю;
- не участвуют в геометрии нового экрана;
- не становятся источником presentation-state;
- имеют конкретный срок удаления внутри PR #16.

---

## 4. Целевая presentation-архитектура

```text
EfironWindow
└─ EfironShell
   ├─ PrimaryNavigation
   ├─ SectionCommandHost
   └─ SectionContentHost
      ├─ LiveTvView
      ├─ GuideView
      │  ├─ GuideChannelView
      │  └─ GuideGridView
      ├─ ChannelsView
      ├─ ArchiveView
      ├─ RecordingsView
      └─ SettingsView
```

### 4.1. Состояние приложения

Новый UI не читает данные из соседних TextBox/ListView.

Вводятся явные presentation-состояния:

- `AppShellState` — текущий раздел, pane и adaptive state;
- `SourceState` — M3U/XMLTV URL, загрузка, ошибка, stale и статистика;
- `ChannelBrowserState` — категории, поиск, выбранный и играющий канал;
- `PlaybackSessionState` — source, opening, playing, paused, error, volume и fullscreen;
- `GuideState` — режим, канал, дата, диапазон времени, поиск и категория;
- `ChannelManagementState` — фильтр, выбранная запись и редактируемый override;
- `AppearanceState` — тема и акцент.

### 4.2. Команды

UI вызывает команды, а не обработчики чужих видимых контролов:

- `LoadPlaylist`;
- `LoadGuide`;
- `PlayChannel`;
- `ToggleFavorite`;
- `SetChannelCategory`;
- `SaveChannelOverride`;
- `SelectGuideDate`;
- `OpenProgramme`;
- `ToggleFullscreen`;
- `SetTheme`;
- `SetAccent`.

### 4.3. Экранные controls

Новые экраны создаются отдельными XAML-controls. `MainWindow.xaml` перестаёт содержать полную разметку всех разделов одновременно.

Обязательные общие компоненты:

- `EfironChannelRow`;
- `EfironChannelLogo`;
- `EfironCategoryRail`;
- `EfironProgrammeRow`;
- `EfironProgrammeProgress`;
- `EfironStatusBadge`;
- `EfironPlayerSurface`;
- `EfironNowNextPanel`;
- `EfironSegmentedControl`;
- `EfironEmptyState`;
- `EfironLoadingState`;
- `EfironErrorState`.

Live TV, EPG и Channels используют один комплект компонентов.

---

## 5. Startup architecture

## 5.1. Запрещённая последовательность

Нельзя до `Window.Activate()`:

- строить все экраны приложения;
- создавать всю EPG-сетку;
- загружать сохранённые источники по сети;
- инициализировать невидимые разделы;
- создавать крупные динамические visual trees;
- блокировать UI ожиданием LibVLC или каталога.

## 5.2. Целевая последовательность

1. Прочитать только язык, тему и минимальные локальные shell-настройки.
2. Создать `EfironWindow` и лёгкий `EfironShell`.
3. Выполнить `Activate()`.
4. Зафиксировать событие первого `Loaded`/первого кадра.
5. Показать skeleton/welcome Live TV.
6. В следующем dispatcher cycle подключить локальный каталог и последний источник.
7. Инициализировать LibVLC только для активного Live TV media surface.
8. Загружать EPG и остальные экраны лениво либо в фоне.

## 5.3. Измерение

Добавляется startup trace с монотонными отметками:

- process/app start;
- application initialized;
- window constructed;
- window activated;
- shell loaded;
- Live view ready;
- LibVLC initialized;
- local catalog ready;
- source refresh started/completed.

CI startup smoke остаётся обязательным, но не заменяет Windows-измерение первого отображения.

30-секундное чёрное окно является блокирующим дефектом.

---

## 6. Первый runnable delivery

Первая новая сборка после этого решения обязана включать одновременно:

### 6.1. Новую оболочку

- утверждённый порядок основной навигации;
- лёгкий content host;
- корректный title bar;
- системную, светлую и тёмную темы;
- выбранный акцент;
- отсутствие технического footer-лога.

### 6.2. `Эфир — Wide`

- категории отдельной левой колонкой;
- каналы отдельной средней колонкой;
- доминирующий плеер справа;
- Now/Next;
- эффективный номер, логотип/плейсхолдер и название канала;
- текущая передача и прогресс;
- независимые Selected и Playing;
- Избранное и Все каналы сверху категорий;
- поиск;
- welcome-state без плейлиста;
- сохранение playback и fullscreen.

### 6.3. Запрещено в первом delivery

- пустые placeholder-разделы Настроек как основное доказательство прогресса;
- видимые M3U/XMLTV поля в Live TV;
- обычный ComboBox вместо category rail в Wide;
- технический список `название + group-title`;
- старый Live layout внутри новой рамки;
- выдавать компиляцию за визуальную приёмку.

---

## 7. Acceptance первого delivery

### Автоматически

- Core tests — success;
- Release x64 — success;
- только `ru-RU` и `en-US`;
- startup smoke — process remains alive;
- startup trace создаётся и не содержит пропущенных обязательных milestones.

### Windows runtime

- shell появляется без длительного чёрного ожидания;
- композиция узнаваемо соответствует утверждённому постеру;
- категории, каналы и плеер имеют утверждённые пропорции;
- плейлист загружается через presentation command;
- канал запускается;
- выбранный и играющий канал различаются;
- Now/Next обновляется;
- fullscreen работает;
- тема и акцент не ломают геометрию;
- нет видимых compatibility controls.

Первый delivery не считается принятым без Windows-видео.

---

## 8. Порядок после первого delivery

1. EPG Channel и Grid на общей presentation-модели.
2. Channels workspace с постоянным редактором.
3. Полноценные Settings Sources/Interface/Player/Control.
4. Medium/Compact и управление с пульта.
5. Удаление всех compatibility bridges и старого MainWindow presentation-кода.

---

## Итоговый verdict

**FULL PRESENTATION-LAYER REFACTOR APPROVED — PHASE 2A REJECTED — FIRST TARGET: APPROVED SHELL + LIVE TV WIDE — MERGE PROHIBITED UNTIL RUNTIME ACCEPTANCE**
