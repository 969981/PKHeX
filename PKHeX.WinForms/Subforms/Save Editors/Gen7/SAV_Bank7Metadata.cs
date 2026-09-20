using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using PKHeX.Core;

namespace PKHeX.WinForms;

/// <summary>
/// Read-only explorer for metadata present in a full Pokémon Bank v1.5 serialized image.
/// </summary>
public sealed class SAV_Bank7Metadata : Form
{
    private readonly Bank7 Bank;
    private readonly byte[] Raw;
    private readonly TextBox RawPreview = new();

    public SAV_Bank7Metadata(Bank7 bank)
    {
        ArgumentNullException.ThrowIfNull(bank);
        if (!bank.IsFullImage)
            throw new ArgumentException("A full Pokémon Bank v1.5 image is required.", nameof(bank));

        Bank = bank;
        Raw = bank.Write().ToArray();

        Text = "Pokémon Bank v1.5 Metadata";
        Icon = Properties.Resources.Icon;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 560);
        Size = new Size(1120, 720);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreateOverviewTab());
        tabs.TabPages.Add(CreateSourceRecordsTab());
        tabs.TabPages.Add(CreateBoxesTab());
        tabs.TabPages.Add(CreateSlotsTab());
        tabs.TabPages.Add(CreateRawTab());
        Controls.Add(tabs);
    }

    private TabPage CreateOverviewTab()
    {
        var grid = CreateGrid("Field", "Value");
        grid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        var meta = Bank.GetHeaderMetadata();
        AddValue(grid, "Image Kind", Bank.ImageKind);
        AddValue(grid, "Serialized Size", $"0x{Raw.Length:X} ({Raw.Length:N0} bytes)");
        AddValue(grid, "Object ID", $"0x{meta.ObjectID:X16}");
        AddValue(grid, "Version", meta.Version);
        AddValue(grid, "Box Count", meta.BoxCount);
        AddValue(grid, "Stored Date/Time", $"{meta.Year:D4}-{meta.Month:D2}-{meta.Day:D2} {meta.Hour:D2}:{meta.Minute:D2}:{meta.Second:D2}");
        AddValue(grid, "Header Padding", $"0x{meta.Padding:X2}");
        AddValue(grid, "Update Gift ID", $"0x{meta.UpdateGiftID:X8} ({meta.UpdateGiftID})");
        AddValue(grid, "Timed Gift ID", $"0x{meta.TimedGiftID:X8} ({meta.TimedGiftID})");
        AddValue(grid, "Points", meta.Points);
        AddValue(grid, "Passes", meta.Passes);
        AddValue(grid, "Flags", Convert.ToHexString(meta.Flags.Span));
        AddValue(grid, "Deposit Count", meta.DepositCount);
        AddValue(grid, "Withdraw Count", meta.WithdrawCount);

        for (int i = 0; i < 10; i++)
            AddValue(grid, $"Group {i + 1} Name", Bank.GetGroupName(i));

        return CreateTab("Overview", grid);
    }

    private TabPage CreateSourceRecordsTab()
    {
        var names = new string[13];
        names[0] = "Index";
        names[1] = "Player Name";
        names[2] = "Sex";
        names[3] = "Trainer ID";
        for (int i = 0; i < 9; i++)
            names[4 + i] = $"Stat {i}";

        var grid = CreateGrid(names);
        for (int i = 0; i < 8; i++)
        {
            var record = Bank.GetSourceRecord(i);
            var values = new object[13];
            values[0] = i;
            values[1] = record.PlayerName;
            values[2] = record.Sex;
            values[3] = $"0x{record.TrainerID:X8} ({record.TrainerID})";
            var statistics = record.Statistics.Span;
            for (int j = 0; j < statistics.Length; j++)
                values[4 + j] = $"0x{statistics[j]:X8} ({statistics[j]})";
            grid.Rows.Add(values);
        }

        return CreateTab("Source Records", grid);
    }

    private TabPage CreateBoxesTab()
    {
        var grid = CreateGrid("Box", "Name", "Background", "Group", "Order", "Gen 6", "Gen 7", "Sources", "Latest Metadata Time");
        grid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        for (int box = 0; box < Bank.BoxCount; box++)
        {
            int gen6 = 0;
            int gen7 = 0;
            var sourceCounts = new int[256];
            ulong latest = 0;
            for (int slot = 0; slot < Bank.BoxSlotCount; slot++)
            {
                var meta = Bank.GetSlotMetadata(box, slot);
                if (meta.FormatTag == 0)
                    gen6++;
                else if (meta.FormatTag == 1)
                    gen7++;
                if (meta.SourceCode != 0)
                    sourceCounts[meta.SourceCode]++;
                if (meta.Timestamp > latest)
                    latest = meta.Timestamp;
            }

            var sources = new StringBuilder();
            for (int code = 1; code < sourceCounts.Length; code++)
            {
                int count = sourceCounts[code];
                if (count == 0)
                    continue;
                if (sources.Length != 0)
                    sources.Append(", ");
                sources.Append(SourceCodeText((byte)code)).Append('×').Append(count);
            }

            grid.Rows.Add(
                box + 1,
                Bank.GetBoxName(box),
                $"0x{Bank.GetBoxBackground(box):X2}",
                Bank.GetBoxGroup(box),
                Bank.GetBoxOrder(box),
                gen6,
                gen7,
                sources.Length == 0 ? "—" : sources.ToString(),
                TimestampText(latest));
        }

        return CreateTab("Boxes", grid);
    }

    private TabPage CreateSlotsTab()
    {
        var grid = CreateGrid("Global", "Box", "Slot", "Species", "Format", "Source", "Time UTC", "Raw Time", "Transfer Format");
        for (int box = 0; box < Bank.BoxCount; box++)
        {
            for (int slot = 0; slot < Bank.BoxSlotCount; slot++)
            {
                int global = Bank7V15Layout.GetSlotIndex(box, slot);
                var meta = Bank.GetSlotMetadata(box, slot);
                var pk = Bank.GetBoxSlotAtIndex(box, slot);
                grid.Rows.Add(
                    global,
                    box + 1,
                    slot + 1,
                    pk.Species,
                    FormatTagText(meta.FormatTag),
                    SourceCodeText(meta.SourceCode),
                    TimestampText(meta.Timestamp),
                    meta.Timestamp == 0 ? "0" : $"0x{meta.Timestamp:X16}",
                    FormatTagText(Bank.GetTransferFormatTag(slot)));
            }
        }

        return CreateTab("Slots", grid);
    }

    private TabPage CreateRawTab()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 360,
        };

        var grid = CreateGrid("Region", "Offset", "Size", "Notes");
        grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        grid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        var regions = new[]
        {
            new BankRegion("Header", 0, Bank7V15Layout.BoxStart, "Object ID, group names, version/count, date and flags"),
            new BankRegion("Boxes", Bank7V15Layout.BoxStart, Bank7V15Layout.TransferBase - Bank7V15Layout.BoxStart, "100 × 0x1B56 box records"),
            new BankRegion("Transfer PKM", Bank7V15Layout.TransferBase, Bank7V15Layout.FormatBase - Bank7V15Layout.TransferBase, "30 × 0xE8 stored Pokémon records"),
            new BankRegion("Format Tags", Bank7V15Layout.FormatBase, Bank7V15Layout.TransferFormatBase - Bank7V15Layout.FormatBase, "3000 parallel slot bytes"),
            new BankRegion("Transfer Format", Bank7V15Layout.TransferFormatBase, Bank7V15Layout.SourceRecordBase - Bank7V15Layout.TransferFormatBase, "Raw transfer-format area"),
            new BankRegion("Source Records", Bank7V15Layout.SourceRecordBase, Bank7V15Layout.SourceRecordSize * 8, "8 × 0x44 source-game summary records"),
            new BankRegion("Aggregate", Bank7V15Layout.AggregateBase, Bank7V15Layout.AggregateSize, "Raw aggregate / Pokédex-like area"),
            new BankRegion("Counters", Bank7V15Layout.CountersBase, Bank7V15Layout.SourceCodeBase - Bank7V15Layout.CountersBase, "Deposit / withdraw counters"),
            new BankRegion("Source Codes", Bank7V15Layout.SourceCodeBase, Bank7V15Layout.TimestampBase - Bank7V15Layout.SourceCodeBase, "3000 parallel slot bytes"),
            new BankRegion("Timestamps", Bank7V15Layout.TimestampBase, Bank7V15Layout.TailBase - Bank7V15Layout.TimestampBase, "3000 × 8-byte raw timestamp values"),
            new BankRegion("Tail", Bank7V15Layout.TailBase, Bank7V15Layout.TailSize, "Reserved / unknown tail bytes"),
        };

        foreach (var region in regions)
        {
            int row = grid.Rows.Add(region.Name, $"0x{region.Offset:X6}", $"0x{region.Size:X} ({region.Size:N0})", region.Notes);
            grid.Rows[row].Tag = region;
        }

        RawPreview.Dock = DockStyle.Fill;
        RawPreview.Multiline = true;
        RawPreview.ReadOnly = true;
        RawPreview.WordWrap = false;
        RawPreview.ScrollBars = ScrollBars.Both;
        RawPreview.Font = new Font(FontFamily.GenericMonospace, 9F);

        grid.SelectionChanged += (_, _) => UpdateRawPreview(grid);
        split.Panel1.Controls.Add(grid);
        split.Panel2.Controls.Add(RawPreview);

        if (grid.Rows.Count != 0)
            grid.Rows[0].Selected = true;
        UpdateRawPreview(grid);

        return CreateTab("Raw Structure", split);
    }

    private void UpdateRawPreview(DataGridView grid)
    {
        if (grid.SelectedRows.Count == 0 || grid.SelectedRows[0].Tag is not BankRegion region)
            return;

        int size = Math.Min(region.Size, Raw.Length - region.Offset);
        if (size <= 0)
        {
            RawPreview.Text = string.Empty;
            return;
        }

        RawPreview.Text = BuildHexDump(Raw.AsSpan(region.Offset, size), region.Offset);
    }

    private static string BuildHexDump(ReadOnlySpan<byte> data, int baseOffset)
    {
        const int maxBytes = 0x1000;
        int count = Math.Min(data.Length, maxBytes);
        var sb = new StringBuilder((count / 16 + 2) * 80);

        for (int i = 0; i < count; i += 16)
        {
            int lineCount = Math.Min(16, count - i);
            sb.Append((baseOffset + i).ToString("X6")).Append("  ");

            for (int j = 0; j < 16; j++)
            {
                if (j < lineCount)
                    sb.Append(data[i + j].ToString("X2")).Append(' ');
                else
                    sb.Append("   ");
            }

            sb.Append(" | ");
            for (int j = 0; j < lineCount; j++)
            {
                byte value = data[i + j];
                sb.Append(value is >= 0x20 and <= 0x7E ? (char)value : '.');
            }
            sb.AppendLine();
        }

        if (data.Length > count)
            sb.AppendLine().Append("… ").Append((data.Length - count).ToString("N0")).Append(" additional bytes not shown in preview.");

        return sb.ToString();
    }

    private static DataGridView CreateGrid(params string[] columns)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            BackgroundColor = SystemColors.Window,
        };

        foreach (var name in columns)
            grid.Columns.Add(name.Replace(' ', '_'), name);
        return grid;
    }

    private static string FormatTagText(byte value) => value switch
    {
        0 => "0x00 (Generation 6)",
        1 => "0x01 (Generation 7)",
        _ => $"0x{value:X2} (Unknown)",
    };

    private static string SourceCodeText(byte value) => value switch
    {
        0 => "0x00 (None / unknown)",
        24 => "0x18 (Pokémon X)",
        25 => "0x19 (Pokémon Y)",
        26 => "0x1A (Pokémon Alpha Sapphire)",
        27 => "0x1B (Pokémon Omega Ruby)",
        30 => "0x1E (Pokémon Sun)",
        31 => "0x1F (Pokémon Moon)",
        32 => "0x20 (Pokémon Ultra Sun)",
        33 => "0x21 (Pokémon Ultra Moon)",
        _ => $"0x{value:X2} (Unknown)",
    };

    private static string TimestampText(ulong value)
    {
        if (value == 0)
            return "—";
        try
        {
            var epoch = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            return epoch.AddSeconds(value).ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
        }
        catch (ArgumentOutOfRangeException)
        {
            return "Out of range";
        }
    }

    private static void AddValue(DataGridView grid, string field, object? value) => grid.Rows.Add(field, value?.ToString() ?? string.Empty);

    private static TabPage CreateTab(string text, Control content)
    {
        var page = new TabPage(text);
        page.Controls.Add(content);
        return page;
    }

    private sealed record BankRegion(string Name, int Offset, int Size, string Notes);
}
