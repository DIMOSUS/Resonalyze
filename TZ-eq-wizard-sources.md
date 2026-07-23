# ТЗ: EQ Wizard — источники кривых (оверлеи, история, txt)

Статус: согласовано с владельцем 2026-07-23. Исполнитель — сессия Claude Code
(Opus 4.8). Прочитай `AGENTS.md` перед началом; конвенции кода и тестов — оттуда.

## 0. Цель

EQ Wizard сейчас принимает только IR из файла. Нужно, чтобы источником кривой
для эквализации могли быть: IR из файла, IR из истории измерений, захваченный
оверлей-слот режима Frequency Response, кривая из текстового файла.

**Главный сценарий:** тюн АЧХ по RTA из Live Spectrum с перемещением микрофона
(moving-mic): пользователь захватывает усреднённую RTA-кривую в оверлей-слот
(часто в dB SPL), открывает EQ Wizard, выбирает слот источником и гонит Auto
Tune. Сегодня этот сценарий не работает дважды: у визарда нет источника-оверлея,
а сам захват RTA не несёт метаданных (см. блок B).

## 1. Зафиксированные решения (не пересматривать)

1. **Импорт-снимок.** Выбор оверлея/истории — одноразовый импорт данных в
   визард. Никакой живой связи с первоисточником: последующие изменения
   слота/истории на визард не влияют. Читаем слот-файлы с диска
   (`OverlayFile.Load`), а не живую `OverlayCollection` — граница из
   `AGENTS.md` («EqWizardPanel is self-contained») сохраняется на уровне
   «не трогаем UI-объекты оверлеев»; обнови формулировку в `AGENTS.md`:
   панель может читать оверлей-*файлы* и снапшоты истории как источники импорта.
2. **Формат оверлея v5 не бампаем.** `RawSpectrum` (некалиброванный спектр) +
   `RawCalibrationCorrectionDb` (замороженная коррекция на дисплейной сетке
   1024 точки) уже дают «сырые точки + калибровка отдельно». Все новые поля —
   только additive с безопасными дефолтами, без изменения `CurrentVersion`.
3. **Без когерентности.** Оверлеи её не хранят и хранить не будут (в главном
   сценарии — RTA — её и нет). Для оверлей-источников Auto Tune гейтит бусты
   только детектом нулей (`EqAutoTuner` это штатно допускает при
   `coherence == null`). Когерентность остаётся только у IR-источников
   (файл/история с loopback-transfer).
4. **Только захваты с метаданными.** В меню слотов попадают только слоты с
   `Kind == Captured`, `CapturedYAxisKey == null` и `CapturedCurveKind` ∈
   {`Primary`, `InputSpectrum`}. Legacy-слоты (`CapturedCurveKind == null`) не
   показываем.
5. **Комбо калибровки — полный набор** для оверлея с raw-данными:
   Off / 0° / 90° / Own («собственная» — замороженная коррекция из захвата).
6. **Явный контрол частоты дискретизации** + частота пишется в метадату
   оверлея при захвате.
7. **Текстовый экспорт/импорт получает `#`-шапку** с метаданными; headerless
   файлы (REW и прочий сторонний обмен) остаются валидными.

## 2. Работы

### A. dsp: `AnalysisCurveKind.InputSpectrum`

Добавить член `InputSpectrum` в `AnalysisCurveKind`
([dsp/AnalysisModels.cs](dsp/AnalysisModels.cs)) — RTA (спектр входа микрофона)
как полноценный вид кривой анализа. Поведенческих изменений в dsp нет
(сериализация оверлеев — строковая, `JsonStringEnumConverter`).

В `CurveTag.BaseLabel` ([source/Plotting/CurveTag.cs](source/Plotting/CurveTag.cs))
добавить случай `InputSpectrum => "Input Spectrum (RTA)"`.

### B. Live Spectrum: RTA — полноценная кривая с raw-захватом

Сейчас RTA-серия несёт строковый тег `live-spectrum:input-magnitude`
([LiveSpectrumController.cs:33](source/LiveSpectrum/LiveSpectrumController.cs)),
поэтому её захват в слот — fallback без метаданных и без raw. Исправить:

1. **Тег.** Повесить на RTA-серию
   `new CurveTag(Mode.LiveSpectrum, AnalysisCurveKind.InputSpectrum, CurveSource.Main)`
   вместо строки. Обновить все места, узнающие серию по строковому тегу
   (снятие live-серий с модели и т.п.). Побочный эффект — RTA становится
   доступна как live-операнд calculated-оверлеев (`GetLiveCurveOptions`) — это
   желаемое поведение, не подавлять.
2. **Raw-провайдер.** `PlotModelFactory.BuildRawCurve` отдаёт raw только для
   `Mode.FrequencyResponse` в Relative
   ([PlotModelFactory.cs:168](source/Plotting/PlotModelFactory.cs)). Добавить
   ветку для RTA (`Mode.LiveSpectrum` + `InputSpectrum`):
   - **Relative:** сырые бины как (Hz, dB) — тот же вход, что у
     `ResampleLiveSpectrumMagnitude`, калибровка отделена по образцу FR-пути
     (спектр некалиброванный, коррекция заморожена через
     `RawCurveRenderer.CaptureCalibrationCorrection`), порядок
     smooth-then-calibrate сохранён.
   - **SPL:** `LogarithmicPowerBandResample` при smoothing = Off и **без**
     калибровки; скалярный SPL-офсет вшить в Y (константа в dB коммутирует со
     сглаживанием); покобиновую коррекцию заморозить отдельно на той же сетке
     1024 (сейчас и коррекция, и офсет применяются после бандинга —
     [PlotModelFactory.cs:951](source/Plotting/PlotModelFactory.cs) — т.е.
     отделимы). `SmoothingCode` = текущее сглаживание Live Spectrum.
   - Данные для raw живут в снапшоте контроллера, а не в фабрике: пусть
     `LiveSpectrumController` кэширует последний `InputMagnitude` и отдаёт
     raw-захват; в `Form1` скомпоновать провайдер
     (`tag => RTA ? controller.BuildRawRtaCapture() : plotModelFactory.BuildRawCurve(tag)`).
   - **Оговорка о точности:** пере-сглаживание уже забандированной SPL-кривой
     через `RawCurveRenderer.Render` — приближение (бандинг при Off + сглаживание
     ≠ бандинг при ширине w). Принято сознательно; проверить визуально, что
     Off воспроизводит экранную кривую, а ширины дают разумную форму.
3. `CapturedMagnitudeScale` для таких захватов уже штатно берётся из
   `EffectiveLiveSpectrumScale` — работ нет.

После этого захват RTA в слот проходит raw-путём `Overlay.CaptureSeries`:
`CapturedCurveKind = InputSpectrum`, `RawSpectrum` заполнен, слот проходит
фильтр визарда.

### C. `OverlayFile` + `RawCurveCapture`: частота дискретизации

- `RawCurveCapture` (source/Overlays/Overlay.cs) — добавить `int? SampleRateHz`.
  Провайдеры заполняют: FR — sample rate измерения (Main/Compare), RTA —
  `noiseMeasurement.SampleRate`.
- `OverlayFile` — additive-поле `public int? SampleRateHz { get; set; }`
  (без бампа версии; старые файлы читаются как null). `Overlay.CaptureSeries`
  записывает его из захвата; fallback-захваты и импорт текста — null.

### D. `OverlayTextFile`: `#`-шапка

Формат (все строки опциональны, порядок свободный):

```
# resonalyze-curve v1
# kind: Primary | InputSpectrum | Deviation | EqCorrection
# scale: Relative | SoundPressureLevel
# sample-rate: 48000
# title: Overlay 3: Magnitude
123.4 -5.5
...
```

- **Export.** Новая перегрузка `Export(path, points, OverlayTextMetadata?)`;
  старая сигнатура остаётся. Вызовы: `ExportToText` слота пишет
  kind/scale/sample-rate/title из состояния слота; `ExportDeviationToText`
  пишет `kind: Deviation` / `EqCorrection`.
- **Import.** Возвращает точки + распарсенные метаданные (nullable-поля).
  Лояльность сохраняется: неизвестные ключи игнорируются, файл без шапки →
  метаданные пустые. Совместимость гарантирована конструкцией: `#`-строки уже
  сейчас отбрасываются парсером пар, старые билды читают новые файлы.
- **Политика визарда для txt:** без шапки — принять (kind неизвестен, считаем
  магнитудой; scale считаем Relative); с шапкой и kind ∉ {Primary,
  InputSpectrum, отсутствует} — отказ с внятным сообщением (deviation/
  correction — не то, что эквализируют). `sample-rate` из шапки предзаполняет
  контрол частоты.

### E. `EqWizardSource` + резолвер (non-UI, тестируемое ядро)

Новый не-UI слой в `source/Tools/Eq/` по образцу
`EqWizardImportExportCoordinator` (логика без WinForms, покрывается
App.Tests):

- `EqWizardCurveSource` — запись: display name, описание для тултипа, вид
  источника (IR / OverlaySlot / TextCurve), для IR — `IImpulseMeasurement` +
  когерентность (перенести туда текущие `CreateMeasurement` /
  `ExtractTransferCoherence` из `EqWizardPanel.Source.cs`), для кривых —
  `RawSpectrum` (nullable), `OwnCalibrationCorrectionDb`, точки (Points слота /
  txt), `MagnitudeScale`, `int? SampleRateHz`, `CapturedCurveKind?`,
  флаги `SupportsCalibration` / `SupportsSmoothing` (истина ⇔ есть raw).
- Резолвер:
  - `ListEligibleSlots()` — перебор `OverlayFile.Load(Mode.FrequencyResponse, 1..12)`
    с фильтром из решения №4. Битые слот-файлы **молча пропускать, НЕ
    кварантинить** (`QuarantineCorruptFile` — прерогатива оверлейного UI;
    визард — read-only потребитель).
  - `TryCreateFromOverlaySlot(OverlayFile)`, `TryCreateFromTextFile(path)`,
    `CreateFromImpulseResponseFile(file)`.
  - Нормализация точек для кривых-источников: сортировка по X, дедупликация
    равных X, отбрасывание нефинитных X. **NaN по Y сохранять** — это
    намеренные разрывы (низкая когерентность), `EqAutoTuner` обрабатывает их
    через `valid[]`, `BuildStats` и deviation-fill уже фильтруют нефинитные
    значения по месту. Текущий `Where(IsFinite(Y))` в `ComputeSourceCurve`
    применять только к IR-пути.
- Offset слота (`Offset` в файле) в импорт **не** тянуть: берём точки без него
  (экранный артефакт разведения кривых; для EQ важен истинный уровень).

Расчёт кривой источника в панели (`GetSourceCurve` ветвится):
- IR — текущий FFT-путь без изменений.
- Оверлей с raw — `RawCurveRenderer.Render(raw, коррекция_по_комбо, сглаживание_комбо)`,
  где коррекция: Off → пусто; Own → сохранённая; 0°/90° → текущие файлы через
  `calibrationResolver`, замороженные `CaptureCalibrationCorrection`.
- Оверлей без raw / txt — точки как есть (комбо погашены).

### F. UI (`EqWizardPanel`)

1. **Меню на `buttonLoadIr`.** Пункты:
   - `Impulse response (file)…` — существующий `LoadIr`.
   - `Impulse response from history ▸` — сабменю из
     `MeasurementHistoryService.Entries` (панель получает сервис как
     `virtualCrossoverPanel.HistoryService` в `Form1.cs`); формат пунктов и
     тултипов — как `PopulateCompareHistoryMenu` (`Form1.Compare.cs`: обрезка
     до 48 симв., `Metadata.BuildToolTipText`). Выбор: `GetSnapshotAsync` →
     `ToImpulseResponseFile()` → общий IR-путь (когерентность и режим
     измерения сохраняются бесплатно). Запись, удалённая между открытием меню
     и кликом, — молчаливый no-op (как в Compare).
   - `Overlay slot ▸` — из `ListEligibleSlots()`, подпись «N: Title»;
     задизейблен, если пусто.
   - `Curve from text file…` — OpenFileDialog `*.txt` → резолвер.
   - Показ меню: на `Click` через `BeginInvoke` + защита от ложного
     `AppFocusChange`-закрытия — воспроизвести паттерн
     `Overlay.CaptureMenuClosing` / `CaptureButtonClick`
     (source/Overlays/Overlay.cs), это грабли безрамочного chrome-окна, уже
     пройденные. Меню пересобирать при каждом открытии, старое диспозить
     (паттерн `ShowCompareMenu`).
2. **Защита от гонок.** `irLoadGeneration` переименовать в
   `sourceLoadGeneration`; бампается каждым выбором источника (включая
   синхронные), асинхронные завершения (файл IR, история) проверяют поколение
   и `IsDisposed` — как сейчас.
3. **Комбо калибровки.** IR — как сейчас (Off/0°/90°). Оверлей с raw — Off /
   0° / 90° / Own, дефолт Own (воспроизводит то, что пользователь видел при
   захвате). Оверлей без raw / txt — комбо задизейблено. «Own» — новый член
   `MicrophoneCalibrationMode`; в комбо он появляется только при
   оверлей-источнике; если персистентное значение Own, а источник IR/пуст —
   откат в Off. `RefreshCalibrationCombo` при смене файлов калибровки обязан
   сохранять выбор Own.
4. **Комбо сглаживания** активно ⇔ IR или оверлей с raw; погашенное = Off.
5. **Контрол частоты дискретизации.** Объявить в `.Designer.cs` (правило
   WinForms/DPI из `AGENTS.md` — никаких контролов из кода). Комбо стандартных
   частот (44100/48000/88200/96000/176400/192000). IR — показывает частоту
   файла, задизейблен; оверлей с `SampleRateHz` — предзаполнен, редактируем;
   txt/нет метадаты — последнее ручное значение (дефолт 48000), редактируем.
   `EqSampleRate` читает контрол (IR принудительно ставит своё). Ручное
   значение персистится в `EqWizardSettings`.
6. **Ось Y.** IR-источник — штатные −80..10 (Absolute −90..20), заголовок
   «dB». Кривая-источник — подгонка под данные: округление вниз/вверх до 10 с
   запасом ~10 dB, Absolute-границы расширить соответственно; при
   `SoundPressureLevel` заголовок «dB SPL». При возврате на IR — восстановить
   штатные пределы.
7. **Target offset после импорта кривой:** средний конечный уровень источника
   в окне From..To, округлённый до 1 dB, с клампом в диапазон контрола →
   `NumericTargetOffset.Value` (обычные события; перезапись персистентного
   значения — осознанно).
8. **Тексты.** `buttonLoadIr.Text` дефолт «Load source…», после выбора — имя
   источника; тултип — полное описание (путь / «Slot 3: …, SPL, 48 kHz» /
   запись истории). `NoIrHint` нейтрализовать («Load a source — an impulse
   response or a measured curve…»). Тултипы в `InitializeToolTips` обновить.
9. **Auto Tune / статистика / экспорт** работают без изменений поверх любой
   исходной кривой; для оверлея/txt `sourceCoherence == null`.

### G. Персистентность

Источник **не** персистится (семантика импорта-снимка): после перезапуска
визард пуст, как сегодня без IR. Новое в `EqWizardSettings`: только ручная
частота дискретизации.

## 3. Тесты (`tests/Resonalyze.App.Tests`, без WinForms)

1. `OverlayFileTests`: roundtrip `SampleRateHz`; старый JSON без поля → null.
2. `OverlayTextFileTests`: экспорт с метадатой пишет шапку; импорт парсит её;
   headerless-файл импортируется как раньше; roundtrip; неизвестный kind в
   шапке → kind «неизвестен», файл принят; строки шапки не попадают в точки.
3. Резолвер: матрица пригодности слотов (Captured+Primary — да;
   Captured+InputSpectrum — да; kind null — нет; Target/Operation — нет;
   coherence-axis — нет); битый слот-файл пропущен и **не** перемещён;
   сортировка/дедуп/NaN-политика; отказ по kind Deviation/EqCorrection из txt;
   прокидка scale/sample-rate.
4. SPL-raw для RTA: вычленить построение (бандинг при Off + отделённая
   коррекция + вшитый офсет) в чистую тестируемую функцию; тест: применение
   коррекции и офсета к raw воспроизводит экранную кривую при Off бит-в-бит.
5. Хелперы подгонки оси и target offset — чистые статики, прямые тесты.

Прогон: `dotnet test` по конвенциям `AGENTS.md`; App.Tests исполняются на
Windows CI (см. `HANDOFF.md`).

## 4. Приёмка

- FR-захват (Relative) → слот → визард: кривая при том же сглаживании
  совпадает с оверлеем; Own-калибровка воспроизводит захват; Off показывает
  сырую.
- Live Spectrum, SPL RTA → захват в слот → визард: слот виден в меню, ось в
  dB SPL и подогнана, target лёг на кривую, Auto Tune отрабатывает, экспорт
  PEQ уходит с частотой из метадаты.
- txt: экспорт слота → импорт в визард → та же кривая; headerless REW-файл
  импортируется.
- Legacy-слот без kind в меню отсутствует; слот с когерентностью отсутствует.
- IR-путь (файл) — поведение не изменилось (регрессия): калибровка 0°/90°,
  когерентность в Auto Tune, ось −80..10.
- IR из истории: когерентность loopback-transfer доезжает до Auto Tune.

## 5. Не делаем (границы)

- Peak Hold как источник — нет: это дисплейная огибающая, не кривая анализа.
  Если понадобится для MMM-по-максимуму — отдельная задача.
- Живая связь визарда с оверлеями/историей — нет (импорт-снимок).
- Хранение когерентности в оверлеях — нет.
- Бамп версии `OverlayFile` / миграции — нет, только additive-поля.
- Персистентность выбранного источника — нет.
