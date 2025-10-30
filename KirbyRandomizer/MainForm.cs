using System;
using System.Collections.Generic;
using System.Globalization;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using Ookii.Dialogs.WinForms;
using System.Security.Cryptography; // (already present if you used it before)
using System.Text.RegularExpressions;

namespace KirbyRandomizer
{
    public partial class MainForm : Form
    {
        uint hitboxPhysStart = 0x081E17;
        uint hitboxPhysEnd = 0x081F85;
        uint hitboxProjStart = 0x08290E;
        uint hitboxProjEnd = 0x082950;

        List<int> elementNormal = new List<int>() { 0x00, 0x01, 0x02, 0x03 };
        List<int> elementSharp = new List<int>() { 0x04, 0x05, 0x06, 0x07 };
        List<int> elementFire = new List<int>() { 0x08, 0x09, 0x0A, 0x0B };
        List<int> elementElectric = new List<int>() { 0x0C, 0x0D, 0x0E, 0x0F };
        List<int> elementIce = new List<int>() { 0x10, 0x11, 0x12, 0x13 };
        List<int> elementNormal2 = new List<int>() { 0x14, 0x15, 0x16, 0x17 };

        uint enemyAbilityStart = 0x10426B;
        uint enemyAbilityEnd = 0x1042A5;
        uint miniBossAbilityStart = 0x1042AB;
        uint miniBossAbilityEnd = 0x1042B1;
        uint bossAbilityStart = 0x1042BA;
        uint bossAbilityEnd = 0x1042CB;
        uint regionOffset = 0xEE;
        uint USid = 0xF4;
        uint JPid = 0xCF;
        uint region = 0x0;

        Random rng = new Random();

        private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();


        private static int NextSecurePositiveSeed()
        {
            // Uniform in [1, int.MaxValue]
            var bytes = new byte[4];
            int v;
            do
            {
                _rng.GetBytes(bytes);
                v = BitConverter.ToInt32(bytes, 0) & int.MaxValue; // clears sign bit
            } while (v == 0);
            return v;
        }


        public MainForm()
        {
            InitializeComponent();
            randSeed.Text = NextSecurePositiveSeed().ToString();
        }

        private void loadMINT_Click(object sender, EventArgs e)
        {
            OpenFileDialog readROM = new OpenFileDialog();
            readROM.DefaultExt = ".smc";
            readROM.AddExtension = true;
            readROM.Filter = "SNES SMC ROM Files|*.smc|All Files|*";
            if (readROM.ShowDialog() == DialogResult.OK)
            {
                filePath.Text = readROM.FileName;
                randSettingsGroup.Enabled = true;
                byte[] fileData = File.ReadAllBytes(readROM.FileName);
                if (fileData[regionOffset] == USid)
                {
                    romRegion.Text = "ROM Region: NTSC";
                }
                if (fileData[regionOffset] == JPid)
                {
                    romRegion.Text = "ROM Region: JPN";
                }
                region = fileData[regionOffset];
            }
        }

        private void randElements_CheckedChanged(object sender, EventArgs e)
        {
            if (randElements.Checked)
            {
                randElementsEach.Enabled = true;
                randOneElement.Enabled = true;
                randElementsHitboxes.Enabled = true;
                if (!randKB.Checked && !randEnemies.Checked)
                {
                    randomize.Enabled = true;
                }
            }
            if (!randElements.Checked)
            {
                randElementsEach.Enabled = false;
                randOneElement.Enabled = false;
                randElementsHitboxes.Enabled = false;
                if (!randKB.Checked && !randEnemies.Checked)
                {
                    randomize.Enabled = false;
                }
            }
        }

        private void randEnemies_CheckedChanged(object sender, EventArgs e)
        {
            if (randEnemies.Checked)
            {
                includeMinorEnemies.Enabled = true;
                randBossAbilities.Enabled = true;
                randMiniBossAbilities.Enabled = true;
                if (!randKB.Checked && !randElements.Checked)
                {
                    randomize.Enabled = true;
                }
            }
            if (!randEnemies.Checked)
            {
                includeMinorEnemies.Enabled = false;
                randBossAbilities.Enabled = false;
                randMiniBossAbilities.Enabled = false;
                if (!randKB.Checked && !randElements.Checked)
                {
                    randomize.Enabled = false;
                }
            }
        }

        private sealed class SeedLogEntry
        {
            public int Seed;
            public string OutputPath;
            public DateTime Stamp;
            public SeedLogEntry(int seed, string outputPath, DateTime stamp)
            {
                Seed = seed;
                OutputPath = outputPath;
                Stamp = stamp;
            }
        }

        private static int GetMaxExistingRandomizedIndex(string folder, string baseName)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return 0;

            int max = 0;
            string prefix = baseName + " Randomized-";
            foreach (var path in Directory.GetFiles(folder, baseName + " Randomized-*.smc"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string tail = name.Substring(prefix.Length);
                    if (int.TryParse(tail, out int n) && n > max)
                        max = n;
                }
            }
            return max;
        }

        private static string GetNextAvailableRandomizedPath(string folder, string baseName)
        {
            int next = GetMaxExistingRandomizedIndex(folder, baseName) + 1;
            return Path.Combine(folder, $"{baseName} Randomized-{next}.smc");
        }

        // Scans for both: 
        //   NEW  =>  baseName(-N)? [seed].smc     (N omitted means index 1)
        //   OLD  =>  baseName Randomized-N.smc
        private static int GetMaxExistingIndex(string folder, string baseName)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return 0;

            int max = 0;
            string escaped = Regex.Escape(baseName);

            // NEW pattern: "[baseName]" or "[baseName]-N", then space, then "[...]", then ".smc"
            var rxNew = new Regex(@"^" + escaped + @"(?:-(\d+))?\s\[[^\]]+\]\.smc$", RegexOptions.IgnoreCase);

            // OLD pattern: "[baseName] Randomized-N.smc"
            var rxOld = new Regex(@"^" + escaped + @"\sRandomized-(\d+)\.smc$", RegexOptions.IgnoreCase);

            foreach (var path in Directory.GetFiles(folder, "*.smc"))
            {
                string file = Path.GetFileName(path);

                var mNew = rxNew.Match(file);
                if (mNew.Success)
                {
                    int n = mNew.Groups[1].Success ? int.Parse(mNew.Groups[1].Value) : 1; // no "-N" => index 1
                    if (n > max) max = n;
                    continue;
                }

                var mOld = rxOld.Match(file);
                if (mOld.Success && int.TryParse(mOld.Groups[1].Value, out int nOld) && nOld > max)
                    max = nOld;
            }
            return max;
        }

        private static string ComposeRandomizedPath(string folder, string baseName, int index, int seed)
        {
            // index==1 => no "-1"
            string indexPart = (index <= 1) ? "" : "-" + index.ToString();
            string fileName = $"{baseName}{indexPart} [{seed}].smc"; // literal brackets in name
            return Path.Combine(folder, fileName);
        }

        private void randomize_Click(object sender, EventArgs e)
        {
            var historyBatch = new List<SeedLogEntry>();
            var writtenPaths = new List<string>();

            // 1) Validate input ROM
            if (string.IsNullOrWhiteSpace(filePath.Text) || !File.Exists(filePath.Text))
            {
                MessageBox.Show("Select a valid base ROM first.", "Kirby Super Star Randomizer", MessageBoxButtons.OK);
                return;
            }

            // How many outputs?
            int count = overwriteROM.Checked ? 1 : (int)nudHowManySeeds.Value;
            try { count = (int)nudHowManySeeds.Value; } catch { /* if control missing, default to 1 */ }

            string baseDir = Path.GetDirectoryName(filePath.Text);
            string baseName = Path.GetFileNameWithoutExtension(filePath.Text);
            string targetFolderForSingle = baseDir; // single-output defaults next to the base ROM

            // 2) Decide where to write
            // Overwrite means: write back to the original ROM path
            bool allowOverwrite = overwriteROM.Checked;
            string singleOutPath = null;
            string folderOutPath = null;

            if (allowOverwrite)
            {
                // write back to the original path later
            }
            else if (count == 1)
            {
                // Single file, keep it simple and mirror your naming scheme with -1
                singleOutPath = Path.Combine(baseDir, $"{baseName} Randomized-1.smc");
            }
            else // count > 1
            {
                using (var dlg = new VistaFolderBrowserDialog
                {
                    Description = "Select output folder for randomized ROMs",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true,
                    SelectedPath = baseDir
                })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    folderOutPath = dlg.SelectedPath;
                }
            }

            int startIndexForBulk = 0;
            if (!allowOverwrite && count > 1)
            {
                startIndexForBulk = GetMaxExistingIndex(folderOutPath, baseName) + 1;
            }



            // 3) Disable UI while working
            bool oldEnabled = this.Enabled;
            this.Enabled = false;

            // Preserve original seed box text; we’ll tweak it per iteration for reproducibility
            string originalSeedText = randSeed.Text;

            try
            {
                for (int i = 1; i <= count; i++)
                {
                    // Decide the seed that will be USED
                    int seed;
                    if (count == 1)
                    {
                        // Single: honor what's in the box; if invalid/empty, generate a fresh one
                        if (!int.TryParse(originalSeedText, out seed) || seed <= 0)
                            seed = NextSecurePositiveSeed();
                        randSeed.Text = seed.ToString(); // ensure your randomizer reads the same seed
                    }
                    else
                    {
                        // Bulk: always fresh crypto-random
                        seed = NextSecurePositiveSeed();
                        randSeed.Text = seed.ToString();
                    }

                    // fresh ROM source each time
                    byte[] ROMdata = File.ReadAllBytes(filePath.Text);

                    // your existing randomization toggles (unchanged)
                    if (randEnemies.Checked)
                    {
                        var tmp = randomizeAbilities(ROMdata);
                        if (tmp == null) { MessageBox.Show("Error: Seed was not in correct format.", "Kirby Super Star Randomizer"); return; }
                        ROMdata = tmp;
                    }
                    if (randElements.Checked)
                    {
                        var tmp = RandomizeHitboxElements(ROMdata);
                        if (tmp == null) { MessageBox.Show("Error: Seed was not in correct format.", "Kirby Super Star Randomizer"); return; }
                        ROMdata = tmp;
                    }
                    if (randKB.Checked)
                    {
                        var tmp = RandomizeHitboxKB(ROMdata);
                        if (tmp == null) { MessageBox.Show("Error: Seed was not in correct format.", "Kirby Super Star Randomizer"); return; }
                        ROMdata = tmp;
                    }

                    // Decide output path for this iteration
                    string outPath;
                    if (allowOverwrite)
                    {
                        // Overwrite keeps writing back to the original path
                        outPath = filePath.Text;
                    }
                    else if (count == 1)
                    {
                        // Single: continue after the highest existing in the input ROM’s folder
                        int nextIndex = GetMaxExistingIndex(baseDir, baseName) + 1;
                        outPath = ComposeRandomizedPath(baseDir, baseName, nextIndex, seed);
                    }
                    else
                    {
                        // Bulk: continue numbering from startIndexForBulk
                        int index = startIndexForBulk + (i - 1);
                        outPath = ComposeRandomizedPath(folderOutPath, baseName, index, seed);
                    }

                    // Write file
                    File.WriteAllBytes(outPath, ROMdata);

                    writtenPaths.Add(Path.GetFullPath(outPath));   // track the real, final path

                    // Record for history
                    historyBatch.Add(new SeedLogEntry(seed, Path.GetFullPath(outPath), DateTime.Now));
                }


                // 4) Done messages
                try { AppendSeedsToHistory(historyBatch); } catch { /* ignore logging errors */ }
                if (writtenPaths.Count == 1)
                {
                    MessageBox.Show(
                        "Successfully created:\n" + writtenPaths[0],
                        "Kirby Super Star Randomizer",
                        MessageBoxButtons.OK
                    );
                }
                else
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"Successfully created {writtenPaths.Count} randomized ROMs:");
                    foreach (var p in writtenPaths) sb.AppendLine(p);
                    MessageBox.Show(sb.ToString(), "Kirby Super Star Randomizer", MessageBoxButtons.OK);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error while generating ROM(s):\n" + ex.Message, "Kirby Super Star Randomizer", MessageBoxButtons.OK);
            }
            finally
            {
                // restore UI and original seed text
                randSeed.Text = originalSeedText;
                this.Enabled = oldEnabled;
            }
        }


        public byte[] randomizeAbilities(byte[] data)
        {
            //Seed stuff
            if (randSeed.Text != "")
            {
                if (int.TryParse(randSeed.Text, out int result))
                {
                    rng = new Random(result);
                }
                else
                {
                    byte[] end = null;
                    return end;
                }
            }
            //Enemies
            if (includeMinorEnemies.Checked)
            {
                for (uint i = enemyAbilityStart; i <= enemyAbilityEnd; i++)
                {
                    data[i] = byte.Parse(rng.Next(0, 24).ToString());
                }
            }
            if (!includeMinorEnemies.Checked)
            {
                List<int> randomAbilityResults = new List<int>();
                List<int> abilityCount = new List<int>()
                {
                    0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
                };
                byte[] origData = data;
                bool ResultOK = false;
                while (!ResultOK)
                {
                    data = origData;
                    rng = new Random();
                    abilityCount = new List<int>()
                    {
                        0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
                    };
                    randomAbilityResults.Clear();
                    //Randomize Abilities
                    for (uint i = 0; i <= 58; i++)
                    {
                        int randomAbility = rng.Next(0, 24);
                        if (data[enemyAbilityStart + i] != 0x00)
                        {
                            data[enemyAbilityStart + i] = byte.Parse(randomAbility.ToString());
                        }
                        randomAbilityResults.Add(randomAbility);
                    }
                    //Check randomization
                    if (randSeed.Text == "")
                    {
                        for (int i = 1; i < randomAbilityResults.Count; i++)
                        {
                            abilityCount[randomAbilityResults[i]] = abilityCount[randomAbilityResults[i]] + 1;
                        }
                        if (!abilityCount.Contains(0))
                        {
                            ResultOK = true;
                        }
                        else
                        {
                            ResultOK = false;
                        }
                        //debug stuff
                        if (ResultOK == false)
                        {
                            Console.WriteLine("Randomization Check: NOT OK");
                        }
                        if (ResultOK == true)
                        {
                            Console.WriteLine("Randomization Check: OK!");
                        }
                    }
                    else
                    {
                        ResultOK = true;
                    }
                }
            }
            //Mini-Bosses
            if (randMiniBossAbilities.Checked)
            {
                for (uint i = miniBossAbilityStart; i <= miniBossAbilityEnd; i++)
                {
                    data[i] = byte.Parse(rng.Next(0, 24).ToString());
                }
            }
            //Bosses
            if (randMiniBossAbilities.Checked)
            {
                for (uint i = bossAbilityStart; i <= bossAbilityEnd; i++)
                {
                    data[i] = byte.Parse(rng.Next(0, 24).ToString());
                }
            }
            return data;
        }

        public byte[] RandomizeHitboxElements(byte[] data)
        {
            //Seed stuff
            if (randSeed.Text != "")
            {
                if (int.TryParse(randSeed.Text, out int result))
                {
                    rng = new Random(result);
                }
                else
                {
                    byte[] end = null;
                    return end;
                }
            }
            //Randomize each Copy Ability
            if (randOneElement.Checked)
            {
                Dictionary<string, int> abilityElements = new Dictionary<string, int>()
                {
                    {"normal", rng.Next(0, 5)},
                    {"cutter", rng.Next(0, 5)},
                    {"beam", rng.Next(0, 5)},
                    {"yo-yo", rng.Next(0, 5)},
                    {"ninja", rng.Next(0, 5)},
                    {"wing", rng.Next(0, 5)},
                    {"fighter", rng.Next(0, 5)},
                    {"jet", rng.Next(0, 5)},
                    {"sword", rng.Next(0, 5)},
                    {"fire", rng.Next(0, 5)},
                    {"stone", rng.Next(0, 5)},
                    {"plasma", rng.Next(0, 5)},
                    {"wheel", rng.Next(0, 5)},
                    {"bomb", rng.Next(0, 5)},
                    {"ice", rng.Next(0, 5)},
                    {"mirror", rng.Next(0, 5)},
                    {"suplex", rng.Next(0, 5)},
                    {"hammer", rng.Next(0, 5)},
                    {"parasol", rng.Next(0, 5)},
                    {"mike", rng.Next(0, 5)},
                    {"paint", rng.Next(0, 5)},
                    {"crash", rng.Next(0, 5)}
                };
                int element = 0;
                for (uint i = hitboxPhysStart; i <= hitboxProjEnd; i++)
                {
                    if (i == hitboxPhysEnd + 1)
                    {
                        i = hitboxProjStart;
                    }
                    if (i == 0x081E17 || i == 0x08290E)
                    {
                        element = abilityElements["normal"];
                    }
                    if (i == 0x081E1A || i == 0x08291B)
                    {
                        element = abilityElements["cutter"];
                    }
                    if (i == 0x081E2B || i == 0x08291E)
                    {
                        element = abilityElements["beam"];
                    }
                    if (i == 0x081E32 || i == 0x082921)
                    {
                        element = abilityElements["yo-yo"];
                    }
                    if (i == 0x081E3C || i == 0x082925)
                    {
                        element = abilityElements["ninja"];
                    }
                    if (i == 0x081E46 || i == 0x082928)
                    {
                        element = abilityElements["wing"];
                    }
                    if (i == 0x081E5A || i == 0x08292B)
                    {
                        element = abilityElements["fighter"];
                    }
                    if (i == 0x081E7D || i == 0x08292F)
                    {
                        element = abilityElements["jet"];
                    }
                    if (i == 0x081E88 || i == 0x082931)
                    {
                        element = abilityElements["sword"];
                    }
                    if (i == 0x081EB2 || i == 0x082933)
                    {
                        element = abilityElements["fire"];
                    }
                    if (i == 0x081EC9 || i == 0x082934)
                    {
                        element = abilityElements["stone"];
                    }
                    if (i == 0x081ECE || i == 0x082935)
                    {
                        element = abilityElements["plasma"];
                    }
                    if (i == 0x081ED1)
                    {
                        element = abilityElements["wheel"];
                    }
                    if (i == 0x08293B)
                    {
                        element = abilityElements["bomb"];
                    }
                    if (i == 0x081EE1 || i == 0x08293C)
                    {
                        element = abilityElements["ice"];
                    }
                    if (i == 0x081EF4 || i == 0x082944)
                    {
                        element = abilityElements["mirror"];
                    }
                    if (i == 0x081EFD)
                    {
                        element = abilityElements["suplex"];
                    }
                    if (i == 0x081F05 || i == 0x081F43)
                    {
                        element = abilityElements["hammer"];
                    }
                    if (i == 0x081F43 || i == 0x081F81)
                    {
                        element = abilityElements["parasol"];
                    }
                    if (i == 0x081F81)
                    {
                        element = abilityElements["mike"];
                    }
                    if (i == 0x081F84)
                    {
                        element = abilityElements["paint"];
                    }
                    if (i == 0x081F85)
                    {
                        element = abilityElements["crash"];
                    }
                    //Rolling Normal
                    if (element == 0)
                    {
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementIce.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Sharp
                    if (element == 1)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementIce.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Fire
                    if (element == 2)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementIce.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Electric
                    if (element == 3)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementIce.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Ice
                    if (element == 4)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Normal2
                    if (element == 5)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementIce.IndexOf(data[i])].ToString());
                        }
                    }
                }
            }
            //Randomize each attack
            if (randElementsEach.Checked)
            {
                int element = 0;
                for (uint i = hitboxPhysStart; i <= hitboxProjEnd; i++)
                {
                    if (i == hitboxPhysEnd + 1)
                    {
                        i = hitboxProjStart;
                    }
                    //Physical Attacks
                    if (i == 0x081E17 || i == 0x081E18 || i == 0x081E18 || i == 0x081E19 || i == 0x081E1A || i == 0x081E1E || i == 0x081E1F || i == 0x081E21 || i == 0x081E2B || i == 0x081E31 || i == 0x081E32 || i == 0x081E3A || i == 0x081E3C || i == 0x081E3E || i == 0x081E42 || i == 0x081E46 || i == 0x081E4E || i == 0x081E54 || i == 0x081E55 || i == 0x081E56 || i == 0x081E5A || i == 0x081E66 || i == 0x081E6A || i == 0x081E6C || i == 0x081E70 || i == 0x081E7D || i == 0x081E81 || i == 0x081E85 || i == 0x081E88 || i == 0x081E8C || i == 0x081E90 || i == 0x081E95 || i == 0x081E9F || i == 0x081EA7 || i == 0x081EAA || i == 0x081E99 || i == 0x081EAA || i == 0x081EB2 || i == 0x081EB3 || i == 0x081EC7 || i == 0x081EC8 || i == 0x081EC9 || i == 0x081ECA || i == 0x081ECB || i == 0x081ECE || i == 0x081ECF || i == 0x081ED0 || i == 0x081ED1 || i == 0x081EE1 || i == 0x081EE9 || i == 0x081EF1 || i == 0x081EF4 || i == 0x081EFC || i == 0x081EFD || i == 0x081EFE || i == 0x081F02 || i == 0x081F05 || i == 0x081F11 || i == 0x081F21 || i == 0x081F31 || i == 0x081F3D || i == 0x081F || i == 0x081F43 || i == 0x081F49 || i == 0x081F4E || i == 0x081F57 || i == 0x081F59 || i == 0x081F5D || i == 0x081F5E || i == 0x081F81 || i == 0x081F82 || i == 0x081F83 || i == 0x081F84 || i == 0x081F85)
                    {
                        element = rng.Next(0, 5);
                    }
                    //Projectiles
                    if (i == 0x08290E || i == 0x082912 || i == 0x082914 || i == 0x082916 || i == 0x08291B || i == 0x08291D || i == 0x08291E || i == 0x082920 || i == 0x082921 || i == 0x082922 || i == 0x082924 || i == 0x082925 || i == 0x082926 || i == 0x082927 || i == 0x082928 || i == 0x08292A || i == 0x08292B || i == 0x08292C || i == 0x08292F || i == 0x082930 || i == 0x082931 || i == 0x082932 || i == 0x082933 || i == 0x082934 || i == 0x082935 || i == 0x082936 || i == 0x082937 || i == 0x082938 || i == 0x082939 || i == 0x08293B || i == 0x08293C || i == 0x082944 || i == 0x082947 || i == 0x082948 || i == 0x08294E || i == 0x082950)
                    {
                        element = rng.Next(0, 5);
                    }
                    //Rolling Normal
                    if (element == 0)
                    {
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementIce.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Sharp
                    if (element == 1)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementIce.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Fire
                    if (element == 2)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementIce.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Electric
                    if (element == 3)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementIce.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Ice
                    if (element == 4)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Normal2
                    if (element == 5)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementIce.IndexOf(data[i])].ToString());
                        }
                    }
                }
            }
            //Randomize each hitbox
            if (randElementsHitboxes.Checked)
            {
                for (uint i = hitboxPhysStart; i <= hitboxPhysEnd; i++)
                {
                    int element = rng.Next(0, 5);
                    //Rolling Normal
                    if (element == 0)
                    {
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementIce.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Sharp
                    if (element == 1)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementIce.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Fire
                    if (element == 2)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementIce.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Electric
                    if (element == 3)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementIce.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Ice
                    if (element == 4)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Normal2
                    if (element == 5)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementIce.IndexOf(data[i])].ToString());
                        }
                    }
                }
                for (uint i = hitboxProjStart; i <= hitboxProjEnd; i++)
                {
                    int element = rng.Next(0, 5);
                    //Rolling Normal
                    if (element == 0)
                    {
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementIce.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Sharp
                    if (element == 1)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementIce.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementSharp[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Fire
                    if (element == 2)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementIce.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementFire[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Electric
                    if (element == 3)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementIce.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementElectric[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Ice
                    if (element == 4)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementNormal2.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementIce[elementNormal2.IndexOf(data[i])].ToString());
                        }
                    }
                    //Rolling Normal2
                    if (element == 5)
                    {
                        if (elementNormal.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementNormal.IndexOf(data[i])].ToString());
                        }
                        if (elementSharp.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementSharp.IndexOf(data[i])].ToString());
                        }
                        if (elementFire.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementFire.IndexOf(data[i])].ToString());
                        }
                        if (elementElectric.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementElectric.IndexOf(data[i])].ToString());
                        }
                        if (elementIce.Contains(data[i]))
                        {
                            data[i] = byte.Parse(elementNormal2[elementIce.IndexOf(data[i])].ToString());
                        }
                    }
                }
            }
            return data;
        }

        public byte[] RandomizeHitboxKB(byte[] data)
        {
            //Seed stuff
            if (randSeed.Text != "")
            {
                if (int.TryParse(randSeed.Text, out int result))
                {
                    rng = new Random(result);
                }
                else
                {
                    byte[] end = null;
                    return end;
                }
            }
            //Randomize each Copy Ability
            if (randKBAbility.Checked)
            {
                Dictionary<string, int> abilityKB = new Dictionary<string, int>()
                {
                    {"normal", rng.Next(0, 3)},
                    {"cutter", rng.Next(0, 3)},
                    {"beam", rng.Next(0, 3)},
                    {"yo-yo", rng.Next(0, 3)},
                    {"ninja", rng.Next(0, 3)},
                    {"wing", rng.Next(0, 3)},
                    {"fighter", rng.Next(0, 3)},
                    {"jet", rng.Next(0, 3)},
                    {"sword", rng.Next(0, 3)},
                    {"fire", rng.Next(0, 3)},
                    {"stone", rng.Next(0, 3)},
                    {"plasma", rng.Next(0, 3)},
                    {"wheel", rng.Next(0, 3)},
                    {"bomb", rng.Next(0, 3)},
                    {"ice", rng.Next(0, 3)},
                    {"mirror", rng.Next(0, 3)},
                    {"suplex", rng.Next(0, 3)},
                    {"hammer", rng.Next(0, 3)},
                    {"parasol", rng.Next(0, 3)},
                    {"mike", rng.Next(0, 3)},
                    {"paint", rng.Next(0, 3)},
                    {"crash", rng.Next(0, 3)}
                };
                int kb = 0;
                for (uint i = hitboxPhysStart; i <= hitboxProjEnd; i++)
                {
                    if (i == hitboxPhysEnd + 1)
                    {
                        i = hitboxProjStart;
                    }
                    if (i == 0x081E17 || i == 0x08290E)
                    {
                        kb = abilityKB["normal"];
                    }
                    if (i == 0x081E1A || i == 0x08291B)
                    {
                        kb = abilityKB["cutter"];
                    }
                    if (i == 0x081E2B || i == 0x08291E)
                    {
                        kb = abilityKB["beam"];
                    }
                    if (i == 0x081E32 || i == 0x082921)
                    {
                        kb = abilityKB["yo-yo"];
                    }
                    if (i == 0x081E3C || i == 0x082925)
                    {
                        kb = abilityKB["ninja"];
                    }
                    if (i == 0x081E46 || i == 0x082928)
                    {
                        kb = abilityKB["wing"];
                    }
                    if (i == 0x081E5A || i == 0x08292B)
                    {
                        kb = abilityKB["fighter"];
                    }
                    if (i == 0x081E7D || i == 0x08292F)
                    {
                        kb = abilityKB["jet"];
                    }
                    if (i == 0x081E88 || i == 0x082931)
                    {
                        kb = abilityKB["sword"];
                    }
                    if (i == 0x081EB2 || i == 0x082933)
                    {
                        kb = abilityKB["fire"];
                    }
                    if (i == 0x081EC9 || i == 0x082934)
                    {
                        kb = abilityKB["stone"];
                    }
                    if (i == 0x081ECE || i == 0x082935)
                    {
                        kb = abilityKB["plasma"];
                    }
                    if (i == 0x081ED1)
                    {
                        kb = abilityKB["wheel"];
                    }
                    if (i == 0x08293B)
                    {
                        kb = abilityKB["bomb"];
                    }
                    if (i == 0x081EE1 || i == 0x08293C)
                    {
                        kb = abilityKB["ice"];
                    }
                    if (i == 0x081EF4 || i == 0x082944)
                    {
                        kb = abilityKB["mirror"];
                    }
                    if (i == 0x081EFD)
                    {
                        kb = abilityKB["suplex"];
                    }
                    if (i == 0x081F05 || i == 0x081F43)
                    {
                        kb = abilityKB["hammer"];
                    }
                    if (i == 0x081F43 || i == 0x081F81)
                    {
                        kb = abilityKB["parasol"];
                    }
                    if (i == 0x081F81)
                    {
                        kb = abilityKB["mike"];
                    }
                    if (i == 0x081F84)
                    {
                        kb = abilityKB["paint"];
                    }
                    if (i == 0x081F85)
                    {
                        kb = abilityKB["crash"];
                    }
                    //Rolling stuff
                    if (elementNormal.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementNormal[kb].ToString());
                    }
                    if (elementSharp.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementSharp[kb].ToString());
                    }
                    if (elementFire.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementFire[kb].ToString());
                    }
                    if (elementElectric.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementElectric[kb].ToString());
                    }
                    if (elementIce.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementIce[kb].ToString());
                    }
                    if (elementNormal2.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementNormal2[kb].ToString());
                    }
                }
            }
            //Randomize each attack
            if (randKBAttacks.Checked)
            {
                int kb = 0;
                for (uint i = hitboxPhysStart; i <= hitboxProjEnd; i++)
                {
                    if (i == hitboxPhysEnd + 1)
                    {
                        i = hitboxProjStart;
                    }
                    //Physical Attacks
                    if (i == 0x081E17 || i == 0x081E18 || i == 0x081E18 || i == 0x081E19 || i == 0x081E1A || i == 0x081E1E || i == 0x081E1F || i == 0x081E21 || i == 0x081E2B || i == 0x081E31 || i == 0x081E32 || i == 0x081E3A || i == 0x081E3C || i == 0x081E3E || i == 0x081E42 || i == 0x081E46 || i == 0x081E4E || i == 0x081E54 || i == 0x081E55 || i == 0x081E56 || i == 0x081E5A || i == 0x081E66 || i == 0x081E6A || i == 0x081E6C || i == 0x081E70 || i == 0x081E7D || i == 0x081E81 || i == 0x081E85 || i == 0x081E88 || i == 0x081E8C || i == 0x081E90 || i == 0x081E95 || i == 0x081E9F || i == 0x081EA7 || i == 0x081EAA || i == 0x081E99 || i == 0x081EAA || i == 0x081EB2 || i == 0x081EB3 || i == 0x081EC7 || i == 0x081EC8 || i == 0x081EC9 || i == 0x081ECA || i == 0x081ECB || i == 0x081ECE || i == 0x081ECF || i == 0x081ED0 || i == 0x081ED1 || i == 0x081EE1 || i == 0x081EE9 || i == 0x081EF1 || i == 0x081EF4 || i == 0x081EFC || i == 0x081EFD || i == 0x081EFE || i == 0x081F02 || i == 0x081F05 || i == 0x081F11 || i == 0x081F21 || i == 0x081F31 || i == 0x081F3D || i == 0x081F || i == 0x081F43 || i == 0x081F49 || i == 0x081F4E || i == 0x081F57 || i == 0x081F59 || i == 0x081F5D || i == 0x081F5E || i == 0x081F81 || i == 0x081F82 || i == 0x081F83 || i == 0x081F84 || i == 0x081F85)
                    {
                        kb = rng.Next(0, 5);
                    }
                    //Projectiles
                    if (i == 0x08290E || i == 0x082912 || i == 0x082914 || i == 0x082916 || i == 0x08291B || i == 0x08291D || i == 0x08291E || i == 0x082920 || i == 0x082921 || i == 0x082922 || i == 0x082924 || i == 0x082925 || i == 0x082926 || i == 0x082927 || i == 0x082928 || i == 0x08292A || i == 0x08292B || i == 0x08292C || i == 0x08292F || i == 0x082930 || i == 0x082931 || i == 0x082932 || i == 0x082933 || i == 0x082934 || i == 0x082935 || i == 0x082936 || i == 0x082937 || i == 0x082938 || i == 0x082939 || i == 0x08293B || i == 0x08293C || i == 0x082944 || i == 0x082947 || i == 0x082948 || i == 0x08294E || i == 0x082950)
                    {
                        kb = rng.Next(0, 5);
                    }
                    //Rolling stuff
                    if (elementNormal.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementNormal[kb].ToString());
                    }
                    if (elementSharp.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementSharp[kb].ToString());
                    }
                    if (elementFire.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementFire[kb].ToString());
                    }
                    if (elementElectric.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementElectric[kb].ToString());
                    }
                    if (elementIce.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementIce[kb].ToString());
                    }
                    if (elementNormal2.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementNormal2[kb].ToString());
                    }
                }
            }
            //Randomize each hitbox
            if (randKBHitboxes.Checked)
            {
                int kb = 0;
                for (uint i = hitboxPhysStart; i <= hitboxPhysEnd; i++)
                {
                    kb = rng.Next(0, 3);
                    //Rolling stuff
                    if (elementNormal.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementNormal[kb].ToString());
                    }
                    if (elementSharp.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementSharp[kb].ToString());
                    }
                    if (elementFire.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementFire[kb].ToString());
                    }
                    if (elementElectric.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementElectric[kb].ToString());
                    }
                    if (elementIce.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementIce[kb].ToString());
                    }
                    if (elementNormal2.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementNormal2[kb].ToString());
                    }
                }
                for (uint i = hitboxPhysStart; i <= hitboxPhysEnd; i++)
                {
                    kb = rng.Next(0, 3);
                    //Rolling stuff
                    if (elementNormal.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementNormal[kb].ToString());
                    }
                    if (elementSharp.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementSharp[kb].ToString());
                    }
                    if (elementFire.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementFire[kb].ToString());
                    }
                    if (elementElectric.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementElectric[kb].ToString());
                    }
                    if (elementIce.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementIce[kb].ToString());
                    }
                    if (elementNormal2.Contains(data[i]))
                    {
                        data[i] = byte.Parse(elementNormal2[kb].ToString());
                    }
                }
            }
            return data;
        }

        private void randKB_CheckedChanged(object sender, EventArgs e)
        {
            if (randKB.Checked)
            {
                randKBAbility.Enabled = true;
                randKBAttacks.Enabled = true;
                randKBHitboxes.Enabled = true;
                if (!randEnemies.Checked && !randElements.Checked)
                {
                    randomize.Enabled = true;
                }
            }
            if (!randKB.Checked)
            {
                randKBAbility.Enabled = false;
                randKBAttacks.Enabled = false;
                randKBHitboxes.Enabled = false;
                if (!randEnemies.Checked && !randElements.Checked)
                {
                    randomize.Enabled = false;
                }
            }
        }

        private void nudHowManySeeds_CommitWhileTyping(object sender, EventArgs e)
        {
            // Convert the typed text to a clamped Value on every edit so ValueChanged semantics apply
            var nud = (NumericUpDown)sender;

            // Try parse the current text; if it’s invalid, do nothing (user may still be typing)
            if (decimal.TryParse(nud.Text, out var typed))
            {
                // clamp to [Minimum, Maximum]
                if (typed < nud.Minimum) typed = nud.Minimum;
                if (typed > nud.Maximum) typed = nud.Maximum;

                if (nud.Value != typed)
                {
                    nud.Value = typed;  // this will trigger nudHowManySeeds_ValueChanged
                }
                else
                {
                    // Ensure the enabled/disabled state of Seed reflects a freshly-typed "1"
                    nudHowManySeeds_ValueChanged(nud, EventArgs.Empty);
                }
            }
        }

        // keep your existing handler, or use this:
        private void nudHowManySeeds_ValueChanged(object sender, EventArgs e)
        {
            bool single = (nudHowManySeeds.Value == 1);
            randSeed.Enabled = single;
            label1.Enabled = single;
        }

        private void overwriteROM_CheckedChanged(object sender, EventArgs e)
        {
            bool overwrite = overwriteROM.Checked;

            // Disable/enable the bulk controls
            lblHowManySeeds.Enabled = !overwrite;
            nudHowManySeeds.Enabled = !overwrite;

            if (overwrite)
            {
                // Force single-output; this also triggers ValueChanged -> re-enables Seed
                if (nudHowManySeeds.Value != 1)
                    nudHowManySeeds.Value = 1;
            }
            else
            {
                // Re-sync Seed enabled/disabled based on current count
                nudHowManySeeds_ValueChanged(nudHowManySeeds, EventArgs.Empty);
            }
        }

        private void AppendSeedsToHistory(IEnumerable<SeedLogEntry> entries)
        {
            if (entries == null) return;

            var byDate = entries
                .GroupBy(e => e.Stamp.Date)
                .OrderBy(g => g.Key)
                .ToList();

            string exeDir = Application.StartupPath;
            string logPath = Path.Combine(exeDir, "seedhistory.txt");

            var lines = File.Exists(logPath)
                ? File.ReadAllLines(logPath).ToList()
                : new List<string>();

            foreach (var group in byDate)
            {
                string header = $"[{group.Key:yyyy-MM-dd}]";
                var newLines = group
                    .OrderBy(e => e.Stamp) // oldest -> newest
                    .Select(e => $"Seed {e.Seed} for {e.OutputPath} at {e.Stamp.ToString("h:mm tt", CultureInfo.InvariantCulture)}")
                    .ToList();

                // find current top header (first non-empty line)
                int firstIdx = lines.FindIndex(l => !string.IsNullOrWhiteSpace(l));
                bool hasTopHeader = firstIdx >= 0 && lines[firstIdx] == header;

                if (hasTopHeader)
                {
                    // append to today's section (scan until next header or EOF)
                    int end = lines.Count;
                    for (int j = firstIdx + 1; j < lines.Count; j++)
                    {
                        string l = lines[j];
                        if (l.Length == 12 && l[0] == '[' && l[11] == ']' &&
                            char.IsDigit(l[1]) && char.IsDigit(l[2]) && char.IsDigit(l[3]) && char.IsDigit(l[4]) &&
                            l[5] == '-' && char.IsDigit(l[6]) && char.IsDigit(l[7]) &&
                            l[8] == '-' && char.IsDigit(l[9]) && char.IsDigit(l[10]))
                        {
                            end = j; break;
                        }
                    }
                    lines.InsertRange(end, newLines);
                }
                else
                {
                    // prepend new date section
                    var prefix = new List<string>();
                    prefix.Add(header);
                    prefix.AddRange(newLines);
                    if (lines.Count > 0) prefix.Add("");
                    prefix.AddRange(lines);
                    lines = prefix;
                }
            }

            File.WriteAllLines(logPath, lines);
        }


        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            //Drag & Drop ROM files reading goes here soon(TM)
        }
    }
}
