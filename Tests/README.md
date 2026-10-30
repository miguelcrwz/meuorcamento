# Meu Orçamento tests

Automated checks for the app. Each one has an expected result; at the end you see how many passed and how many failed.

## Running

From the project folder (`MeuOrcamento\`):

```powershell
.\test.ps1
```

Or directly: `dotnet run --project Tests -c Release`.

Run it **after any change** to the app, before publishing the `.exe`. If something fails, the command exits with an error and prints the line in red.

During the "single window" test the app really opens for 2 or 3 seconds and closes on its own. The other test windows open off screen.

## What is checked

| File | What it covers |
|---|---|
| `NumberBoxTests.cs` | Reading values: `1.234,56`, `45.90`, `5.000`, invalid text, minimum and maximum, rounding, rejected keys |
| `RepositoryTests.cs` | Creating and copying months (recurring ones, the 31st in February), cents on save and load, deleting a month with a backup copy, file conflicts, locked or malformed files, and reading files saved by earlier versions |
| `TransactionEditorTests.cs` | Drawer validations, overspend warning, editing without duplicating, suggestions and the description autocomplete; installments: total ↔ installment, rounding cents, the following months, a purchase already under way, turning it back into a single amount, deleting from a month on |
| `AllocationEditorTests.cs` | % ↔ R$, fixed amounts, the 7-category limit, empty and repeated names, and that renaming a category doesn't rewrite past months |
| `WindowTests.cs` | Pages, planning a month, the drawer (error, two-click confirmation, Enter on destructive actions), negative balance, overspend by cents, one window per file |
| `ScreenTests.cs` | Captures every screen and drawer, tests the minimum window, values in the millions, long names, 40 transactions, first run and zero income, and **audits the fonts**: any text outside the type scale is a failure |

## Results

In `Tests\output\` (the folder is recreated on every run):

- `report.txt`: every check with OK or FAIL
- `fonts.txt`: every font combination found on the screens, with examples
- `screens\*.png`: each screen and drawer as it rendered
- `data\`: the test data files, which are fake (the tests **never** open your `dados.json`)

With `.\test.ps1 -Real`, the app also opens a **copy** of your data file and saves the screens to `Tests\output-real\`. The original is never touched.

## When the type scale changes

The list of allowed combinations lives in `ScreenTests.cs`, in the `Scale` field, and mirrors the top of `Themes\Styles.xaml`. If you change the scale on purpose, update both.
