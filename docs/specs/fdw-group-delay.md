# ТЗ: Group delay через частотно-зависимое окно (FDW)

Две поставки на одном DSP-ядре:

- **Режим Group Delay** (вкладка Phase and Group Delay) получает селектор
  **Window: Fixed / FDW** и **FDW cycles** — те же, что у Phase.
- **Virtual DSP, главный (акустический) график** получает четвёртый режим
  **Group delay** рядом с Magnitude / Phase / Impulse: измеренная групповая
  задержка каждого канала и их суммы через окно фазового гейта проекта.

Ветка: `claude/fdw-8-group-delay-plots-gvy765`.

## 1. Зачем

Сегодня GD читается только через фиксированный Tukey-гейт
(`DataHelper.GetGroupDelayCurves`, `dsp/DataHelper.Phase.cs`). На ВЧ такой гейт
длиной 10–40 мс держит весь хвост отражений, и кривая GD в салоне «плывёт» от
сиденья к сиденью. Окно FDW-8 (то, через которое уже читаются Junction phase и
Sum loss (direct)) на сетке из семи позиций референсной машины сократило
межсидельный разброс GD с 2.3 мс до 0.4–0.8 мс (ремарка в
`source/Tools/VirtualCrossover/SumLossWindow.cs`). Эту кривую нигде не рисуют.

В `REFERENCE.md` (раздел «Phase and Group Delay») записано, что FDW к GD
сознательно не применяется: FDW-фаза «не является точным интегралом»
фиксированного GD, и Fixed-фаза + GD объявлены «совместимой парой». После этой
работы совместимой парой становятся одинаковые настройки окна на обеих вкладках;
абзац переписывается (см. §4.6).

## 2. Определение FDW-GD (математика)

Текущий GD считается через тождество для одного окна:

    τ(f) = Re[ T(f) · conj(H(f)) ] / |H(f)|²,   H = FFT(w·h),  T = FFT(t·w·h)

где `t` — время от начала буфера извлечения, затем числитель и энергия
сглаживаются раздельно (энергетическое взвешивание), τ переводится в абсолютное
время добавлением `extractionStart / fs`.

FDW-спектр (`BuildFdwSpectrum`) — это банк спектров через окна длиной
`left + cycles/f_c` на центрах `f_c` (3 на октаву), сшитый **комплексно-линейной**
интерполяцией по log-частоте: `H_fdw = (1−t)·H_lo + t·H_hi`. Так как FFT
линейно, `H_fdw(f)` — спектр IR через интерполированное окно
`w_f = (1−t)·w_lo + t·w_hi`.

**Определение.** FDW-GD на частоте `f` — энергетический центр времени прихода
внутри окна `w_f`:

    T_fdw = (1−t)·T_lo + t·T_hi   (тот же t на том же бине, что и для H)
    τ_fdw(f) = Re[ T_fdw · conj(H_fdw) ] / |H_fdw|²

Это **не** равно `−dφ/dω` нарисованной FDW-фазы: у производной есть лишний член
от скольжения окна по частоте. Тождественное определение физичнее (время
прихода доминирующей энергии в окне из `cycles` периодов) и сохраняет ту же
энергетическую логику, что и текущая Fixed-кривая. Записать это в doc-комментарий
метода и в `REFERENCE.md`.

**Привязка ко времени.** Каждый элемент банка `k` извлечён с `extractionStart_k`
(`s_k`). Для `H` разница стартов снимается фазовым поворотом
(`ApplyTimeReference`). Для `T` перед поворотом нужно сдвинуть временной вес:

    T_k' = T_k + ((s_k − s_ref) / fs) · H_k,   затем тот же поворот, что и у H_k

В текущем банке `left = min(requestedLeft, gate)` всегда равен `requestedLeft`
(минимальное окно ≥ `left + 0.8 мс`), поэтому все элементы имеют один
`extractionStart` и поправка равна нулю. Реализовать общую форму всё равно —
тест §3.6.5 фиксирует инвариант, чтобы он не зависел от этой случайности.

**Сумма каналов (VDSP).** Сумма читается из суммы спектров, не из гейта над
суммарной IR (та же причина, что у фазового Sum в `BuildPhaseCurves`):

    H_sum = Σ rot_k(H_k),   T_sum = Σ rot_k(T_k + ((s_k − s_ref)/fs)·H_k)

Линейность обоих операторов даёт GD суммы, согласованный с GD каналов.

**Порог сглаживания.** Сейчас минимальная полуширина сглаживания —
`0.5·fs/gateSamples` от длины фиксированного гейта. Под FDW разрешение на `f`
определяется эффективным окном на этой частоте:

    T_eff(f) = clamp(left + cycles/f, minimumGate, fixedGate) [в секундах]
    minHalfWidthHz(f) = 0.5 / T_eff(f)

При 8 циклах без клампа это `f/16` — около ±1/11 октавы, то есть шире порога
стабилизации 1/48 и сравнимо с отображаемым сглаживанием 1/12 по умолчанию.
Следствие для документации: под FDW сглаживание уже 1/12 октавы выше частоты
перехода почти ничего не меняет. Функция эффективного окна должна быть ОДНА на
банк и на порог (вынести из `BuildFdwSpectrum`), и тест сверяет их.

## 3. Часть A — DSP-ядро (`dsp/Resonalyze.Dsp`)

Кроссплатформенно, собирается и тестируется на Linux.

### 3.1 API

1. `DataHelper.GetGroupDelayCurves(IImpulseMeasurement, PhaseAnalysisSettings settings, double smoothingInverseOctaves, double magnitudeGateDb, bool includeMinimumPhase)` —
   новая основная перегрузка. `settings.WindowMode == Fixed` даёт результат,
   **побитно** равный сегодняшнему (регрессионный тест старая-vs-новая
   сигнатура). Существующая перегрузка с `leftMs/plateauMs/rightMs` остаётся как
   обёртка над Fixed, чтобы не трогать вызовы, которым FDW не нужен.
   `DetrendMode`/`Unwrap`/`Smoothing` внутри `settings` игнорируются
   (документировать).
2. `DataHelper.GetGroupDelayAnalysisSpectra(IImpulseMeasurement, PhaseAnalysisSettings, out int extractionStart)`
   → `(Complex[] Spectrum, Complex[] TimeWeighted)` — пара `(H, T)` для одного
   канала в одной временной привязке. Нужна VDSP, которому надо складывать
   каналы до вычисления τ.
3. `DataHelper.SumGatedSpectraPairs(IReadOnlyList<(Complex[] H, Complex[] T, int Start)> parts, int targetStart)`
   → `(H, T)` с поправкой временного веса из §2. Существующий
   `SumGatedSpectra` не меняется.
4. `DataHelper.GetGroupDelayCurves((Complex[] H, Complex[] T), int extractionStart, int sampleRate, PhaseAnalysisSettings settings, double smoothingInverseOctaves, double magnitudeGateDb, bool includeMinimumPhase, MeasuredBand? band)`
   — построение кривых из готовой пары; `settings` нужны только для порога
   сглаживания (§2). Перегрузка 1 реализуется через 2 + 4.
5. `internal static double FdwEffectiveGateSamples(double frequencyHz, PhaseAnalysisSettings settings, int sampleRate)` —
   единая функция эффективного окна, используемая банком и порогом сглаживания.
6. `SmoothBinsHann` получает перегрузку с `Func<double, double> minHalfWidthHz`
   (или `double[]` по бинам); скалярная версия остаётся.

### 3.2 Банк с временным двойником

`BuildFdwSpectrum` обобщается в `BuildFdwSpectrumPair`: для каждого элемента
банка две FFT (`w·h` и `n·w·h/fs`, `n` — индекс в буфере, как в Fixed-ветке),
поправка веса и поворот, сшивка обеих пар одним `t` на бин, сопряжённая
симметрия для обеих. Fixed-ветка строит пару так же, как сегодня.

### 3.3 Кэш

`PhaseSpectrumCache` (per-impulse `ConditionalWeakTable`) расширяется записью
`(H, T, extractionStart)` с флагом `TimeWeighted` в ключе. Фазовый вызов
продолжает получать только `H`; GD-вызов на уже посчитанном банке добирает `T`
(или считает пару сразу — на усмотрение реализации, но повторный redraw VDSP с
переключением видимости не должен пересчитывать FFT).

### 3.4 Минимальная фаза и excess

Без изменений по конструкции: `ComputeMinimumPhaseGroupDelayNumerator` берёт
`|H_fdw|`. Excess = measured − minimum. Документировать, что под FDW «минимальная»
часть считается от магнитуды прямого звука, а не steady-state.

### 3.5 Валидность и полоса

Тот же локальный энергетический гейт (`magnitudeGateDb`, −60 dB backstop).
`MeasuredBand` (`LowestMeasuredFrequencyHz`/`HighestMeasuredFrequencyHz` на
`ImpulseMeasurementView`) режет точки вне измеренной полосы в NaN — как у
магнитуд VDSP.

### 3.6 Тесты (`tests/Resonalyze.Dsp.Tests`)

Новый файл `FrequencyDependentGroupDelayTests.cs`; существующие
`GroupDelayCurvesTests` проходят без изменений.

1. **Регрессия Fixed.** Старая и новая сигнатуры при `WindowMode = Fixed`
   дают побитно равные три кривые.
2. **Чистая задержка, 4/6/8 циклов.** τ плоская и равна задержке ±0.01 мс на
   100 Гц…10 кГц; равна Fixed-кривой в той же полосе.
3. **Полный кламп = Fixed.** Гейт короче `cycles/f` на всём диапазоне
   (аналог `Fdw_WhenEveryWindowIsClamped_MatchesFixed`) → FDW-GD == Fixed-GD
   побиново.
4. **Прямой звук + позднее отражение.** Импульс + копия через 6 мс на −6 dB,
   гейт 1/10/3 мс. Fixed-GD выше ~1.3 кГц имеет размах ≥ 1 мс (интерференция),
   FDW-8 там плоская на времени прямого прихода ±0.1 мс; ниже частоты, где
   `cycles/f > 6 мс`, обе кривые совпадают в пределах допуска.
5. **Временная привязка.** Синтетический банк с двумя элементами разных
   `extractionStart` (через `internal` вход) даёт непрерывную τ без ступеньки;
   поправка `((s_k − s_ref)/fs)·H_k` протестирована отдельно на чистой
   задержке.
6. **Суперпозиция.** Два канала с общим окном: GD из
   `SumGatedSpectraPairs` равна GD из гейта над суммарной IR (±1e-9 мс).
   С разными окнами (Auto per-curve): GD суммы двух чистых задержек `d1, d2`
   с энергиями `e1, e2` равна `(e1·d1 + e2·d2)/(e1+e2)` на частотах, где обе
   попадают в окно.
7. **Минимальная фаза.** Минимально-фазовая система (существующий генератор)
   через FDW-8: excess ≈ 0; all-pass: дисперсия в excess, minimum ≈ 0.
8. **Порог сглаживания.** `FdwEffectiveGateSamples` на центрах банка равен
   `EffectiveGateSamples` записей банка; между центрами монотонен; порог на
   1 кГц при 8 циклах и без клампа равен 62.5 Гц.
9. **Валидность.** Все три кривые бланкуются в одних бинах под обоими режимами
   (теория над существующим `ValidityGate_BlanksTheSameBinsInEveryCurve`).

## 4. Часть B — режим Group Delay (`source/`)

### 4.1 Настройки

`FrequencyResponseOptions` (`dsp/DataHelper.cs`) получает
`GroupDelayWindowMode : PhaseWindowMode` и `GroupDelayFdwCycles : int`.

Умолчания: **FDW, 8 циклов** для свежей установки. Файл настроек, записанный до
появления полей, открывается на **Fixed** — та же миграция, что для фазы в
`MeasurementSettingsFile.cs` (строки ~87–92): пользователь продолжает видеть
ту кривую, которую видел. (Решение владельца; см. §7.)

`MeasurementSettingsFile.Schema.cs`, `FrequencyResponseSettings`:
`PhaseWindowMode? GroupDelayWindowMode` (nullable, как `MagnitudeWindowMode`),
`int GroupDelayFdwCycles`; `Capture`/`ApplyTo` переносят оба поля. История
(`MeasurementSessionSnapshot.GroupDelay`) переезжает автоматически через тот же
record.

### 4.2 GDOpt

В `GDOpt` (`source/Options/GDOpt.cs`, Designer) добавляются `comboWindowMode`
(Fixed / FDW) и `comboFdwCycles` (4 / 6 / 8) по образцу `PROpt`: cycles активен
только под FDW; кнопки R сбрасывают на умолчания; тултипы; `Init`/`SetOptions`
пишут и читают новые поля. Read-out «lowest reliable frequency» остаётся —
гейт по-прежнему внешний предел окна.

### 4.3 PlotModelFactory.CreateGroupDelay

Собрать `PhaseAnalysisSettings` из GD-опций (offset после Auto-snap,
left/plateau/right, window mode, cycles, `DetrendMode = Off`, `Unwrap = false`,
smoothing 0) и вызвать перегрузку §3.1.1. Compare-оверлей читается через те же
режим и число циклов, с той же per-curve постановкой под Auto, что и сегодня.
Комментарий про «gate positioned by its Gate offset» дополнить словом о том, что
под FDW `cycles/f` отсчитываются после левого плеча, как у фазы.

Все остальные вызовы `GetGroupDelayCurves(` в `source/` (`Overlays/`,
`AgentBridge`) проверить: math-оверлеи режима GD должны читать через те же
настройки, что и главная кривая; `BuildExcessGroupDelayCurve` в
`VirtualCrossoverPanel.AgentBridge.cs` — см. открытый вопрос §7.2.

### 4.4 Вьюпорт

Смена Fixed/FDW не меняет смысла оси (по-прежнему мс), но меняет кривую и
авто-подгон Y. Повторить то, что `PROpt` делает при смене режима окна фазы
(`Form1.ModeSettings.cs`, вызов `plotViewports.Forget`): если там `Forget`
вызывается — вызывать и здесь; если нет — не вызывать.

### 4.5 Тесты (`tests/Resonalyze.App.Tests`)

- `PlotModelFactoryTests`: GD-модель под FDW-8 строит три серии; под Fixed —
  результат равен сегодняшнему (снимок точек до/после на синтетике).
- Настройки: round-trip новых полей; файл без полей открывается на Fixed;
  свежие `FrequencyResponseOptions` — FDW-8.
- `GDOptTests` (по образцу `PROptTests`): cycles активен только под FDW;
  `SetOptions` пишет оба поля.

### 4.6 Документация (в том же коммите)

`REFERENCE.md`, «Phase and Group Delay»:

- абзац «Phase additionally offers Window: Fixed / FDW…» → «Phase and Group
  Delay offer…», единый текст про банк окон;
- абзац «FDW is deliberately not applied here…» заменить: что означает FDW-GD
  (энергетический центр прихода внутри окна из `cycles` периодов; прямой звук
  на СЧ/ВЧ, полный гейт на НЧ), что это не производная FDW-фазы и почему,
  что «совместимая пара» — одинаковые режим и число циклов на обеих вкладках;
  что под FDW сглаживание уже 1/12 октавы выше частоты перехода почти не
  влияет (порог разрешения окна); умолчание FDW-8 и поведение старых файлов.
- `MANUAL.md` — только если владелец решит рекомендовать FDW-GD в шаге проверки
  стыков (§7.4); иначе не трогать.

## 5. Часть C — Virtual DSP, четвёртый режим главного графика

### 5.1 UI и персистенция

- `radioViewGroupDelay` («Group delay») после `radioViewImpulse` в
  `VirtualCrossoverPanel.Designer.cs`; `AcousticView.GroupDelay`;
  `CurrentAcousticView()`, `OnViewModeChanged`, `OnViewChanged`, загрузка проекта
  (`radioView*.Checked` из файла) — по образцу Impulse.
- `VirtualCrossoverProjectFile`: аддитивный `bool ShowGroupDelayView`.
  Приоритет при чтении: `ShowImpulseView` > `ShowGroupDelayView` >
  `ShowPhaseView` > magnitude. При записи GD-вида ставить и
  `ShowPhaseView = true`, чтобы старая сборка открыла проект на ближайшем виде
  (Phase), как `ShowImpulseView` сегодня опирается на magnitude/phase. Версия
  файла не меняется (поле аддитивно); `Validate` — если он проверяет
  комбинации флагов, добавить новый.
- Sum-тоггл: `bool? ShowSumCurveGroupDelay` с fallback на `ShowSumCurveOnPhase`
  (тот же приём, что `ShowSumCurvePhase` → `ShowSumCurve`);
  `ApplySumToggleForView`/`OnViewChanged` учитывают четвёртый вид.
- Что глушится на этом виде, как на Phase: Sum loss селектор, Hybrid, Target,
  spatial average. Smoothing-комбо активен, но пункт «psychoacoustic» для GD
  означает 1/12 (как в `BuildExcessGroupDelayCurve`, через
  `SmoothingPresetOptions.Normalize(…, includePsychoacoustic: false)`).
- Групповые виды (`VirtualCrossoverGroupViews.DrawsGroupSums`) глушат GD-радио
  так же, как Phase и Impulse (нет формы GD для группы).

### 5.2 Окно и кривые

- Окно — фазовый гейт проекта в точности как у `BuildPhaseCurves`: постановка
  через `PhaseGatePlacement` (pinned или Auto per-curve по фронту канала),
  Tukey-длительности, `project.PhaseWindowMode`, `project.PhaseFdwCycles`;
  `gatePreview` (открытый диалог Gate…) управляет видом так же, как фазовым.
  Тем самым GD-вид и Phase-вид — одно окно, и GD является «парой» к нарисованной
  фазе. Число циклов — селектора проекта (4/6/8), а не жёсткие 8 из
  `JunctionPhaseSpectra`: это кривая для глаза, как и фаза; жёсткие 8 — правило
  для чисел (см. §7.3).
- Detrend не применяется: GD абсолютная, от отсчёта 0 записи — та же шкала,
  что у Impulse-вида; общее τ фазового вида лишь сдвинуло бы все кривые.
- Набор каналов: drawn set для трасс, summed subset для Sum (та же логика, что
  у фазы, включая скрытые суммирующие каналы при включённом Sum). Спектры
  считаются один раз на redraw через §3.1.2 (`AsParallel().AsOrdered()`),
  Sum — через §3.1.3 к минимальному `extractionStart`.
- Рисуется только измеренная GD канала и Sum (без minimum/excess: вид про
  относительный приход; excess остаётся задачей AI-пробы). Толщины и цвета как у
  фазы (1.8 канал, 2.4 Sum). Полоса `MeasuredBand` канала режет кривую; для Sum
  — объединение полос. Гейт валидности `GroupDelayMagnitudeGateDb` (−40 dB)
  режима GD; на стоп-полосах кроссоверов кривая обрывается — это желаемое.
- Все данные проекта и гейта снимаются на UI-потоке до воркеров (правило
  `BuildPhaseCurves`).

### 5.3 Ось и презентер (`VirtualCrossoverAcousticPlot`)

- `ConfigureForView(GroupDelay)`: ось значений «ms», линейная,
  `Minimum/Maximum = NaN` (авто), `AbsoluteMinimum/Maximum` сняты, zoom/pan
  включены (не замок, как у фазы); нижняя ось — log-частота; loss-ось скрыта.
- Авто-подгон Y по валидным точкам измеренных кривых (как
  `UpdateGroupDelayRange` в `PlotModelFactory`), с небольшим запасом; зум
  пользователя сохраняется между redraw по действующему правилу
  (`VirtualCrossoverAcousticPlotZoomTests`).
- Tracker: `"{0}\n{2:0.0} Hz\n{4:0.000} ms"`.

### 5.4 Вне объёма

Тюнинг-лист PDF, AI-пакет, `AgentDiagnosticBuilder`, Auto delay, метрики —
без изменений (кроме §7.2).

### 5.5 Тесты (`tests/Resonalyze.App.Tests`)

- `VirtualCrossoverProjectFileTests`: round-trip `ShowGroupDelayView` и
  `ShowSumCurveGroupDelay`; файл без полей открывается прежним видом;
  GD-файл в старом чтении (флаг проигнорирован) даёт Phase.
- `VirtualCrossoverAcousticPlotZoomTests`: конфигурация оси для GD; зум
  переживает redraw; переключение Phase→GD→Phase возвращает замок ±180°.
- Рендер на синтетике (по образцу тестов фазового вида, `SyntheticArrayHarness`):
  два канала — чистые задержки `d1`, `d2` — дают плоские кривые на `d1`, `d2`;
  Sum между ними по энергиям; скрытие канала не двигает окна остальных
  (placement по gated set).
- Опционально, не в CI: прогон `SessionBatteryHarness` по архивным кабинам с
  межсидельным разбросом GD под Fixed и FDW-8 — воспроизвести цифру 3–5×.

### 5.6 Документация (в том же коммите)

`REFERENCE.md`, «Virtual DSP»:

- список видов главного графика: добавить **Group delay** — что рисует, через
  какое окно, что Sum — сумма спектров, что шкала абсолютная (та же, что у
  Impulse), какие тогглы на нём заглушены, как ведёт себя Sum-тоггл;
- «Graph Zoom and Limits» (абзац про две оси VDSP со своим правилом, строки
  ~137–141): дописать правило GD-оси (свободный зум, авто-подгон по измеренным
  кривым);
- «The panel: gates, plots and read-outs»: «shape the phase and impulse views
  only» → «phase, group-delay and impulse views»; связать с абзацем про Sum loss
  (direct) и цифрой 3–5×: GD-вид — это та самая кривая, чей разброс там
  измерялся;
- `MANUAL.md` — см. §7.4.

## 6. Порядок работ

Три коммита/PR, каждый с документацией по правилу `AGENTS.md`:

1. Часть A (DSP + тесты) — собирается и проверяется на Linux:
   `dotnet test tests/Resonalyze.Dsp.Tests/Resonalyze.Dsp.Tests.csproj`.
2. Часть B (режим GD) — Windows-сборка; `Resonalyze.App.Tests`.
3. Часть C (VDSP) — Windows-сборка; `Resonalyze.App.Tests`.

Предупреждения = ошибки, подавлений нет (`Directory.Build.props`).

## 7. Открытые вопросы (решает владелец)

1. **Умолчание режима GD для свежей установки.** Предложено FDW-8 (старые
   файлы — Fixed). Альтернатива: Fixed везде, FDW — по выбору.
2. **AI-проба `excessGroupDelay`.** `PROTOCOL.md` описывает её как «через
   фазовый гейт проекта», но код читает только длительности (Fixed). После
   появления GD-вида, читающего режим проекта, расхождение станет видимым.
   Предложено: перевести пробу на `project.PhaseWindowMode`/`PhaseFdwCycles` в
   Части C и поправить строку таблицы в `docs/agent/PROTOCOL.md`; либо явно
   записать в протокол, что проба читает Fixed.
3. **Число циклов в VDSP GD-виде.** Предложено — селектор проекта (кривая для
   глаза, единое окно с фазовым видом). Альтернатива — жёсткие 8, как у чисел
   (`JunctionPhaseSpectra.FdwCycles`), тогда под 4/6 циклами фаза и GD читаются
   через разные окна.
4. **`MANUAL.md`.** Менять ли шаг проверки стыков (рекомендовать GD-вид VDSP
   как проверку прихода после Auto delay)? По правилу `AGENTS.md` мануал
   правится только когда меняется, что делает тюнер.
5. **Minimum/excess в VDSP.** Предложено не рисовать. Если нужны — это два
   дополнительных тоггла и `includeMinimumPhase: true` на каждом канале
   (кепстральная реконструкция ×N каналов на redraw).
