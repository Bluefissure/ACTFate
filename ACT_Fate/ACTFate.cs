// reference:System.dll
// reference:System.Core.dll
// reference:System.Web.Extensions.dll
using System;
using System.Reflection;
using Advanced_Combat_Tracker;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Xml;
using System.Text;
using System.Net;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Forms;

[assembly: AssemblyTitle("FFXIV F.A.T.E")]
#if COMPATIBLE
[assembly: AssemblyDescription("Duty FATE Assist -- ACT Plugin (Compatible)")]
#else
[assembly: AssemblyDescription("Duty FATE Assist -- ACT Plugin")]
#endif
[assembly: AssemblyCompany("Bluefissure")]
[assembly: AssemblyVersion("1.3.0.0")]

namespace FFXIV_FATE_ACT_Plugin
{
    public class ACTFate : System.Windows.Forms.UserControl, IActPluginV1
    {
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Timer timer;
        private bool active = false;
        private FileInfo fileInfo;
        string settingsFile = Path.Combine(ActGlobals.oFormActMain.AppDataFolder.FullName, "Config\\ACTFate.config.xml");
        SettingsSerializer xmlSettings;

        private class ProcessNet
        {
            public readonly Process process;
            public readonly App.Network network;
            public ProcessNet(Process process, App.Network network)
            {
                this.process = process;
                this.network = network;
            }
        }
        private ConcurrentDictionary<int, ProcessNet> networks = new ConcurrentDictionary<int, ProcessNet>();
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ComboBox comboBoxLanguage;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.TextBox postURL;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.CheckBox checkBoxUploader;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TreeView FateTreeView;
        private System.Windows.Forms.CheckBox checkBoxDutyFinder;
        private System.Windows.Forms.CheckBox checkBoxToastNotification;
        private System.Windows.Forms.Button testToastButton;
        private System.Windows.Forms.CheckBox checkBoxTTS;
        private System.Windows.Forms.Button resetCheckedButton;
        private ComboBox comboBoxBookmark;
        private static string APP_ID = "Advanced Combat Tracker"; // You can write anything.
        public ACTFate()
        {
            InitializeComponent();
        }

        public void InitPlugin(System.Windows.Forms.TabPage pluginScreenSpace, System.Windows.Forms.Label pluginStatusText)
        {

            // ShortCutCreator.TryCreateShortcut(APP_ID, APP_ID); // deprecated
            active = true;
            this.lblStatus = pluginStatusText;
            this.lblStatus.Text = "FFXIV F.A.T.E Plugin Started.";

#if COMPATIBLE
            pluginScreenSpace.Text = "FATE Parser (Compatible)";
#else
            pluginScreenSpace.Text = "FATE Parser";
#endif
            pluginScreenSpace.Controls.Add(this);
            xmlSettings = new SettingsSerializer(this);

            foreach (ActPluginData plugin in ActGlobals.oFormActMain.ActPlugins)
            {
                if (plugin.pluginObj != this) continue;
                fileInfo = plugin.pluginFile;
                break;
            }


            if (timer == null)
            {
                timer = new System.Windows.Forms.Timer();
                timer.Interval = 30 * 1000;
                timer.Tick += Timer_Tick;
            }
            timer.Enabled = true;

            updateFFXIVProcesses();
            loadJSONData();
            LoadSettings();
            this.comboBoxLanguage.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            selLng = this.comboBoxLanguage.SelectedValue.ToString();
            loadFates();
            loadBookmarks();
        }

        void LoadSettings()
        {
            xmlSettings.AddControlSetting(comboBoxLanguage.Name, comboBoxLanguage);
            xmlSettings.AddControlSetting(checkBoxToastNotification.Name, checkBoxToastNotification);
            xmlSettings.AddControlSetting(checkBoxTTS.Name, checkBoxTTS);
            xmlSettings.AddControlSetting(checkBoxUploader.Name, checkBoxUploader);
            xmlSettings.AddControlSetting(postURL.Name, postURL);
            xmlSettings.AddControlSetting(checkBoxDutyFinder.Name, checkBoxDutyFinder);
            xmlSettings.AddBooleanSetting("cheatRoulette");
            xmlSettings.AddStringSetting("chkFates");

            if (File.Exists(settingsFile))
            {
                FileStream fs = new FileStream(settingsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                XmlTextReader xReader = new XmlTextReader(fs);
                try
                {
                    while (xReader.Read())
                    {
                        if (xReader.NodeType == XmlNodeType.Element)
                        {
                            if (xReader.LocalName == "SettingsSerializer")
                            {
                                xmlSettings.ImportFromXml(xReader);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    lblStatus.Text = "Error loading settings: " + ex.Message;
                }
                xReader.Close();
            }
            isUploaderEnable = checkBoxUploader.Checked;
            postURL.Enabled = !isUploaderEnable;
            isTTSEnable = checkBoxTTS.Checked;
            isDutyAlertEnable = checkBoxDutyFinder.Checked;
            isToastNotificationEnable = checkBoxToastNotification.Checked;
        }

        IActPluginV1 GetFFXIVPlugin()
        {
            IActPluginV1 obj = null;
            foreach (var x in ActGlobals.oFormActMain.ActPlugins)
            {
                if (x.pluginFile.Name.ToUpper() == "FFXIV_ACT_Plugin.dll".ToUpper() && x.cbEnabled.Checked)
                {
                    obj = x.pluginObj;
                }
            }
            return obj;
        }

        void SaveSettings()
        {
            //tree
            chkFates = "";
            List<string> c = new List<string>();
            foreach (System.Windows.Forms.TreeNode area in this.FateTreeView.Nodes)
            {
                if (area.Checked) c.Add((string)area.Tag);
                foreach (System.Windows.Forms.TreeNode fate in area.Nodes)
                {
                    if (fate.Checked) c.Add((string)fate.Tag);
                }
            }
            chkFates = string.Join("|", c);

            FileStream fs = new FileStream(settingsFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            XmlTextWriter xWriter = new XmlTextWriter(fs, Encoding.UTF8);
            xWriter.Formatting = Formatting.Indented;
            xWriter.Indentation = 1;
            xWriter.IndentChar = '\t';
            xWriter.WriteStartDocument(true);
            xWriter.WriteStartElement("Config");    // <Config>
            xWriter.WriteStartElement("SettingsSerializer");    // <Config><SettingsSerializer>
            xmlSettings.ExportToXml(xWriter);   // Fill the SettingsSerializer XML
            xWriter.WriteEndElement();  // </SettingsSerializer>
            xWriter.WriteEndElement();  // </Config>
            xWriter.WriteEndDocument(); // Tie up loose ends (shouldn't be any)
            xWriter.Flush();    // Flush the file buffer to disk
            xWriter.Close();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (active == false) return;

            updateFFXIVProcesses();
        }

        private void updateFFXIVProcesses()
        {
            var processes = new List<Process>();
            processes.AddRange(Process.GetProcessesByName("ffxiv"));
            processes.AddRange(Process.GetProcessesByName("ffxiv_dx11"));

            for (var i = 0; i < processes.Count; i++)
            {
                Process process = processes[i];
                try
                {
                    if (networks.ContainsKey(process.Id)) continue;
                    ProcessNet pn = new ProcessNet(process, new App.Network());
                    pn.network.onReceiveEvent += Network_onReceiveEvent;
                    networks.TryAdd(process.Id, pn);
                }
                catch (Exception e)
                {
                    Log.Ex(e, "error");
                }
            }

            List<int> toDelete = new List<int>();
            foreach (KeyValuePair<int, ProcessNet> entry in networks)
            {
                if (entry.Value.process.HasExited)
                {
                    entry.Value.network.StopCapture();
                    toDelete.Add(entry.Key);
                }
                else
                {
                    if (entry.Value.network.IsRunning)
                    {
                        entry.Value.network.UpdateGameConnections(entry.Value.process);
                    }
                    else
                    {
                        entry.Value.network.StartCapture(entry.Value.process);
                    }
                }
            }
            for (var i = 0; i < toDelete.Count; i++)
            {
                try
                {
                    ProcessNet pn;
                    networks.TryRemove(toDelete[i], out pn);
                    pn.network.onReceiveEvent -= Network_onReceiveEvent;
                }
                catch (Exception e)
                {
                    Log.Ex(e, "error");
                }
            }


        }

        public void DeInitPlugin()
        {
            active = false;
            Log.richTextBox1 = null;
            if (this.lblStatus != null)
            {
                this.lblStatus.Text = "FFXIV F.A.T.E Plugin Unloaded.";
                this.lblStatus = null;
            }

            foreach (KeyValuePair<int, ProcessNet> entry in networks)
            {
                entry.Value.network.StopCapture();
            }

            timer.Enabled = false;
            SaveSettings();
        }

        private void InitializeComponent()
        {
            this.label1 = new System.Windows.Forms.Label();
            this.comboBoxLanguage = new System.Windows.Forms.ComboBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.postURL = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.checkBoxUploader = new System.Windows.Forms.CheckBox();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.resetCheckedButton = new System.Windows.Forms.Button();
            this.label4 = new System.Windows.Forms.Label();
            this.FateTreeView = new System.Windows.Forms.TreeView();
            this.checkBoxDutyFinder = new System.Windows.Forms.CheckBox();
            this.checkBoxToastNotification = new System.Windows.Forms.CheckBox();
            this.testToastButton = new System.Windows.Forms.Button();
            this.checkBoxTTS = new System.Windows.Forms.CheckBox();
            this.comboBoxBookmark = new System.Windows.Forms.ComboBox();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(21, 17);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(53, 12);
            this.label1.TabIndex = 7;
            this.label1.Text = "Language";
            // 
            // comboBoxLanguage
            // 
            this.comboBoxLanguage.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBoxLanguage.FormattingEnabled = true;
            this.comboBoxLanguage.Location = new System.Drawing.Point(88, 14);
            this.comboBoxLanguage.Name = "comboBoxLanguage";
            this.comboBoxLanguage.Size = new System.Drawing.Size(121, 20);
            this.comboBoxLanguage.TabIndex = 6;
            this.comboBoxLanguage.SelectedIndexChanged += new System.EventHandler(this.comboBoxLanguage_SelectedIndexChanged_1);
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.postURL);
            this.groupBox1.Controls.Add(this.label2);
            this.groupBox1.Controls.Add(this.checkBoxUploader);
            this.groupBox1.Location = new System.Drawing.Point(23, 49);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(533, 51);
            this.groupBox1.TabIndex = 9;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Event Uploader";
            // 
            // postURL
            // 
            this.postURL.Location = new System.Drawing.Point(42, 20);
            this.postURL.Name = "postURL";
            this.postURL.Size = new System.Drawing.Size(478, 21);
            this.postURL.TabIndex = 7;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(15, 23);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(23, 12);
            this.label2.TabIndex = 6;
            this.label2.Text = "URL";
            // 
            // checkBoxUploader
            // 
            this.checkBoxUploader.AutoSize = true;
            this.checkBoxUploader.Location = new System.Drawing.Point(112, -1);
            this.checkBoxUploader.Name = "checkBoxUploader";
            this.checkBoxUploader.Size = new System.Drawing.Size(60, 16);
            this.checkBoxUploader.TabIndex = 5;
            this.checkBoxUploader.Text = "Active";
            this.checkBoxUploader.UseVisualStyleBackColor = true;
            this.checkBoxUploader.CheckedChanged += new System.EventHandler(this.checkBox_CheckedChanged);
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.comboBoxBookmark);
            this.groupBox2.Controls.Add(this.resetCheckedButton);
            this.groupBox2.Controls.Add(this.label4);
            this.groupBox2.Controls.Add(this.FateTreeView);
            this.groupBox2.Controls.Add(this.checkBoxDutyFinder);
            this.groupBox2.Location = new System.Drawing.Point(23, 115);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(533, 457);
            this.groupBox2.TabIndex = 10;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Alert";
            // 
            // resetCheckedButton
            // 
            this.resetCheckedButton.Location = new System.Drawing.Point(84, 52);
            this.resetCheckedButton.Name = "resetCheckedButton";
            this.resetCheckedButton.Size = new System.Drawing.Size(75, 23);
            this.resetCheckedButton.TabIndex = 11;
            this.resetCheckedButton.Text = "Reset";
            this.resetCheckedButton.UseVisualStyleBackColor = true;
            this.resetCheckedButton.Click += new System.EventHandler(this.resetCheckedButton_Click);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(15, 57);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(47, 12);
            this.label4.TabIndex = 10;
            this.label4.Text = "F.A.T.E";
            // 
            // FateTreeView
            // 
            this.FateTreeView.CheckBoxes = true;
            this.FateTreeView.Location = new System.Drawing.Point(15, 81);
            this.FateTreeView.Name = "FateTreeView";
            this.FateTreeView.Size = new System.Drawing.Size(508, 370);
            this.FateTreeView.TabIndex = 9;
            this.FateTreeView.AfterCheck += new System.Windows.Forms.TreeViewEventHandler(this.fateTreeView_AfterCheck);
            // 
            // checkBoxDutyFinder
            // 
            this.checkBoxDutyFinder.AutoSize = true;
            this.checkBoxDutyFinder.Checked = true;
            this.checkBoxDutyFinder.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBoxDutyFinder.Location = new System.Drawing.Point(15, 20);
            this.checkBoxDutyFinder.Name = "checkBoxDutyFinder";
            this.checkBoxDutyFinder.Size = new System.Drawing.Size(90, 16);
            this.checkBoxDutyFinder.TabIndex = 8;
            this.checkBoxDutyFinder.Text = "Duty Finder";
            this.checkBoxDutyFinder.UseVisualStyleBackColor = true;
            this.checkBoxDutyFinder.CheckedChanged += new System.EventHandler(this.checkBoxTelegramDutyFinder_CheckedChanged);
            // 
            // checkBoxToastNotification
            // 
            this.checkBoxToastNotification.AutoSize = true;
            this.checkBoxToastNotification.Location = new System.Drawing.Point(222, 17);
            this.checkBoxToastNotification.Name = "checkBoxToastNotification";
            this.checkBoxToastNotification.Size = new System.Drawing.Size(174, 16);
            this.checkBoxToastNotification.TabIndex = 11;
            this.checkBoxToastNotification.Text = "Active Toast Notification";
            this.checkBoxToastNotification.UseVisualStyleBackColor = true;
            this.checkBoxToastNotification.CheckedChanged += new System.EventHandler(this.checkBoxToastNotification_CheckedChanged);
            // 
            // testToastButton
            // 
            this.testToastButton.Location = new System.Drawing.Point(468, 13);
            this.testToastButton.Name = "testToastButton";
            this.testToastButton.Size = new System.Drawing.Size(75, 23);
            this.testToastButton.TabIndex = 12;
            this.testToastButton.Text = "Test";
            this.testToastButton.UseVisualStyleBackColor = true;
            this.testToastButton.Click += new System.EventHandler(this.testToastButton_Click);
            // 
            // checkBoxTTS
            // 
            this.checkBoxTTS.AutoSize = true;
            this.checkBoxTTS.Location = new System.Drawing.Point(408, 17);
            this.checkBoxTTS.Name = "checkBoxTTS";
            this.checkBoxTTS.Size = new System.Drawing.Size(42, 16);
            this.checkBoxTTS.TabIndex = 13;
            this.checkBoxTTS.Text = "TTS";
            this.checkBoxTTS.UseVisualStyleBackColor = true;
            this.checkBoxTTS.CheckedChanged += new System.EventHandler(this.checkBoxTTS_CheckedChanged);
            // 
            // comboBoxBookmark
            // 
            this.comboBoxBookmark.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBoxBookmark.FormattingEnabled = true;
            this.comboBoxBookmark.Location = new System.Drawing.Point(199, 53);
            this.comboBoxBookmark.Name = "comboBoxBookmark";
            this.comboBoxBookmark.Size = new System.Drawing.Size(121, 20);
            this.comboBoxBookmark.TabIndex = 12;
            this.comboBoxBookmark.SelectedIndexChanged += new System.EventHandler(this.comboBoxBookmark_SelectedIndexChanged);
            // 
            // ACTFate
            // 
            this.Controls.Add(this.checkBoxTTS);
            this.Controls.Add(this.testToastButton);
            this.Controls.Add(this.checkBoxToastNotification);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.comboBoxLanguage);
            this.Name = "ACTFate";
            this.Size = new System.Drawing.Size(1744, 592);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }


        private string getTextInstance(int code)
        {
            try
            {
                return data["instances"][code.ToString()]["name"][selLng].ToString();
            }
            catch (Exception e)
            {
                Log.Ex(e, "ignore");
            }
            return code.ToString();
        }
        private string getTextFate(int code)
        {
            try
            {
                var item = data["fates"][code.ToString()]["name"];
                item = item[selLng].ToString() == "" ? item["en"] : item[selLng];
                return item.ToString();
            }
            catch (Exception e)
            {
                Log.Ex(e, "ignore");
            }
            return code.ToString();
        }
        private string getTextFateArea(int code)
        {
            string areaCode = null;
            try
            {
                areaCode = data["fates"][code.ToString()]["area_code"].ToString();
                return data["areas"][areaCode][selLng].ToString();
            }
            catch (Exception e)
            {
                Log.Ex(e, "ignore");
            }
            return areaCode == null ? code.ToString() : areaCode;
        }
        private string getTextRoulette(int code)
        {
            try
            {
                if (code == 0) return "";
                return data["roulettes"][code.ToString()][selLng].ToString();
            }
            catch (Exception e)
            {
                Log.Ex(e, "ignore");
            }
            return code.ToString();
        }
        private string getFinderTextNotification(int roulette, int code)
        {
            try
            {
                if (roulette != 0)
                {
                    if (cheatRoulette)
                        return getTextRoulette(roulette) + " >> " + getTextInstance(code);
                    else
                        return getTextRoulette(roulette);
                }
                else
                    return getTextInstance(code);
            }
            catch (Exception e)
            {
                Log.Ex(e, "ignore");
            }
            return roulette.ToString() + " >> " + code.ToString();
        }


        private void Network_onReceiveEvent(int pid, App.Network.EventType eventType, int[] args)
        {
            string server = (networks[pid].process.MainModule.FileName.Contains("KOREA") ? "KOREA" : "GLOBAL");
            string text = "[ACTFATE]" + ((char)007) + pid + ((char)007) + server + ((char)007) + eventType + ((char)007);


            int pos = 0;
            switch (eventType)
            {
                case App.Network.EventType.INSTANCE_ENTER:
                case App.Network.EventType.INSTANCE_EXIT:
                    if (args.Length > 0)
                    {
                        text += getTextInstance(args[0]) + ((char)007); pos++;
                    }
                    break;
                case App.Network.EventType.FATE_BEGIN:
                case App.Network.EventType.FATE_PROGRESS:
                case App.Network.EventType.FATE_END:
                    text += getTextFate(args[0]) + ((char)007) + getTextFateArea(args[0]) + ((char)007); pos++;
                    break;
                case App.Network.EventType.MATCH_BEGIN:
                    text += (App.Network.MatchType)args[0] + ((char)007); pos++;
                    switch ((App.Network.MatchType)args[0])
                    {
                        case App.Network.MatchType.ROULETTE:
                            text += getTextRoulette(args[1]) + ((char)007); pos++;
                            break;
                        case App.Network.MatchType.SELECTIVE:
                            text += args[1] + ((char)007); pos++;
                            int p = pos;
                            for (int i = p; i < args.Length; i++)
                            {
                                text += getTextInstance(args[i]) + ((char)007); pos++;
                            }
                            break;
                    }
                    break;
                case App.Network.EventType.MATCH_END:
                    text += (App.Network.MatchEndType)args[0] + ((char)007); pos++;
                    break;
                case App.Network.EventType.MATCH_PROGRESS:
                    text += getTextInstance(args[0]) + ((char)007); pos++;
                    break;
                case App.Network.EventType.MATCH_ALERT:
                    text += getTextRoulette(args[0]) + ((char)007); pos++;
                    text += (args[1].ToString() + ((char)007).ToString());
                    text += getTextInstance(args[1]) + ((char)007); pos++;
                    break;
            }
            for (int i = pos; i < args.Length; i++)
            {
                text += args[i] + ((char)007);
            }

            //sendToACT(text);
            postToToastWindowsNotificationIfNeeded(server, eventType, args);
            postToURLIfNeeded(server, eventType, args);
            postToTTSIfNeeded(server, eventType, args);
        }

        private void sendToACT(string text)
        {
            ActGlobals.oFormActMain.ParseRawLogLine(false, DateTime.Now, "00|" + DateTime.Now.ToString("O") + "|0048|F|" + text);
        }


        private class Language
        {
            public string Name { get; set; }
            public string Code { get; set; }

            public override string ToString()
            {
                return Name;
            }
        }

        private class Bookmark
        {
            public string Name { get; set; }
            public string Code { get; set; }
            public override string ToString()
            {
                return Name;
            }
        }

        private JObject data;
        private string selLng;

        private bool isUploaderEnable = false;
        private string chkFates;
        private bool cheatRoulette;
        private ConcurrentStack<string> SelectedFates = new ConcurrentStack<string>();

        private void loadJSONData()
        {
            string jsonString = File.ReadAllText(fileInfo.Directory.FullName + "/data.json");
            var json = JObject.Parse(jsonString);

            List<Language> languages = new List<Language>();
            var l = json["languages"];
            foreach (var item in l)
            {
                string key = ((JProperty)item).Name;
                languages.Add(new Language { Name = l[key].ToString(), Code = key });
            }


            this.comboBoxLanguage.DataSource = languages.ToArray();
            comboBoxLanguage.DisplayMember = "Name";
            comboBoxLanguage.ValueMember = "Code";
            selLng = comboBoxLanguage.SelectedValue.ToString();

            data = json;

        }

        private void loadBookmarks()
        {
            List<Bookmark> bookmarks = new List<Bookmark>();
            var bms = data["bookmarks"];
            foreach (var item in bms)
            {
                string key = ((JProperty)item).Name;
                bookmarks.Add(new Bookmark { Name = bms[key]["names"][selLng].ToString(), Code = key });
            }
            this.comboBoxBookmark.DataSource = bookmarks;
            comboBoxBookmark.DisplayMember = "Name";
            comboBoxBookmark.ValueMember = "Code";
        }

        private void comboBoxLanguage_SelectedIndexChanged(object sender, EventArgs e)
        {
            selLng = comboBoxLanguage.SelectedValue.ToString();
            loadFates();
        }

        private void loadFates()
        {
            this.FateTreeView.Nodes.Clear();

            List<string> c = new List<string>();
            if (chkFates != null && chkFates != "")
            {
                string[] sp = chkFates.Split(new char[] { '|' });
                for (int i = 0; i < sp.Length; i++)
                {
                    c.Add(sp[i]);
                }
            }

            if(data == null)
                loadJSONData();

            lockTreeEvent = true;
            foreach (JProperty item in data["areas"])
            {
                try
                {
                    string key = item.Name;
                    if (data["areas"][key][selLng].ToString() == "null")
                        continue;
                    System.Windows.Forms.TreeNode areaNode = this.FateTreeView.Nodes.Add(data["areas"][key][selLng].ToString());
                    areaNode.Tag = "AREA:" + key;
                    if (c.Contains((string)areaNode.Tag)) areaNode.Checked = true;
                    foreach (JProperty fate in data["fates"])
                    {
                        if (data["fates"][fate.Name]["area_code"].ToString().Equals(key) == false) continue;
                        string text = data["fates"][fate.Name]["name"][selLng].ToString();
                        if (text == null || text == "") text = data["fates"][fate.Name]["name"]["en"].ToString();
                        System.Windows.Forms.TreeNode fateNode = areaNode.Nodes.Add(text);
                        fateNode.Tag = fate.Name;
                        if (c.Contains((string)fateNode.Tag)) fateNode.Checked = true;
                    }
                }
                catch (Exception e)
                {
                    Log.Ex(e, "error");
                }

            }
            SelectedFates.Clear();
            updateSelectedFates(FateTreeView.Nodes);
            lockTreeEvent = false;

        }

        bool lockTreeEvent = false;
        private bool isDutyAlertEnable;
        private bool isToastNotificationEnable;
        private bool isTTSEnable;

        private void fateTreeView_AfterCheck(object sender, System.Windows.Forms.TreeViewEventArgs e)
        {
            if (lockTreeEvent) return;
            lockTreeEvent = true;
            if (((string)e.Node.Tag).Contains("AREA:"))
            {
                foreach (System.Windows.Forms.TreeNode node in e.Node.Nodes)
                {
                    node.Checked = e.Node.Checked;
                }
            }
            else
            {
                if (e.Node.Checked == false)
                {
                    e.Node.Parent.Checked = false;
                }
                else
                {
                    bool flag = true;
                    foreach (System.Windows.Forms.TreeNode node in e.Node.Parent.Nodes)
                    {
                        flag &= node.Checked;
                    }
                    e.Node.Parent.Checked = flag;
                }
            }
            SelectedFates.Clear();
            updateSelectedFates(FateTreeView.Nodes);


            lockTreeEvent = false;
        }

        private void updateSelectedFates(System.Windows.Forms.TreeNodeCollection nodes)
        {
            foreach (System.Windows.Forms.TreeNode node in nodes)
            {
                if (node.Checked) SelectedFates.Push((string)node.Tag);
                updateSelectedFates(node.Nodes);
            }
        }

        private void postToURLIfNeeded(string server, App.Network.EventType eventType, int[] args)
        {
            if (eventType != App.Network.EventType.FATE_BEGIN && eventType != App.Network.EventType.MATCH_ALERT) return;
            if (isUploaderEnable == false) return;

            string head = networks.Count <= 1 ? "" : "[" + server + "] ";

            switch (eventType)
            {
                case App.Network.EventType.MATCH_ALERT:
                    //text += getTextRoulette(args[0]) + "|"; pos++;
                    //text += getTextInstance(args[1]) + "|"; pos++;
                    if (isDutyAlertEnable)
                    {
                        postToURL(head + getFinderTextNotification(args[0], args[1]));
                    }
                    break;
                case App.Network.EventType.FATE_BEGIN:
                    //text += getTextFate(args[0]) + "|" + getTextFateArea(args[0]) + "|"; pos++;
                    if (SelectedFates.Contains(args[0].ToString()))
                    {
                        postToURL(head + getTextFateArea(args[0]) + " >> " + getTextFate(args[0]));
                    }
                    break;

            }
        }
        private void postToToastWindowsNotificationIfNeeded(string server, App.Network.EventType eventType, int[] args)
        {
            if (eventType != App.Network.EventType.FATE_BEGIN && eventType != App.Network.EventType.MATCH_ALERT) return;
            if (isToastNotificationEnable == false) return;

            string head = networks.Count <= 1 ? "" : "[" + server + "] ";
            switch (eventType)
            {
                case App.Network.EventType.MATCH_ALERT:
                    //text += getTextRoulette(args[0]) + "|"; pos++;
                    //text += getTextInstance(args[1]) + "|"; pos++;
                    if (isDutyAlertEnable)
                    {
                        toastWindowNotification(head + getFinderTextNotification(args[0], args[1]));
                    }
                    break;
                case App.Network.EventType.FATE_BEGIN:
                    //text += getTextFate(args[0]) + "|" + getTextFateArea(args[0]) + "|"; pos++;
                    if (SelectedFates.Contains(args[0].ToString()))
                    {
                        toastWindowNotification(head + getTextFateArea(args[0]) + " >> " + getTextFate(args[0]));
                    }
                    break;

            }
        }

        private void postToTTSIfNeeded(string server, App.Network.EventType eventType, int[] args)
        {
            if (eventType != App.Network.EventType.FATE_BEGIN && eventType != App.Network.EventType.MATCH_ALERT) return;
            if (isTTSEnable == false) return;

            string head = networks.Count <= 1 ? "" : "[" + server + "] ";
            switch (eventType)
            {
                case App.Network.EventType.MATCH_ALERT:
                    //text += getTextRoulette(args[0]) + "|"; pos++;
                    //text += getTextInstance(args[1]) + "|"; pos++;
                    if (isDutyAlertEnable)
                    {
                        TTS(head + getFinderTextNotification(args[0], args[1]));
                    }
                    break;
                case App.Network.EventType.FATE_BEGIN:
                    //text += getTextFate(args[0]) + "|" + getTextFateArea(args[0]) + "|"; pos++;
                    if (SelectedFates.Contains(args[0].ToString()))
                    {
                        TTS(head + getTextFateArea(args[0]) + " " + getTextFate(args[0]));
                    }
                    break;

            }
        }

        private void postToURL(string message)
        {
            string url = postURL.Text;
            if (url == null || url == "") return;
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.UploadValuesAsync(new Uri(url), "POST", new NameValueCollection()
                {
                    { "text", message }
                });
                }
            }
            catch (Exception e)
            {
                Log.Ex(e, "ignore");
            }

        }

        private void checkBox_CheckedChanged(object sender, EventArgs e)
        {
            isUploaderEnable = checkBoxUploader.Checked;
            postURL.Enabled = !isUploaderEnable;
        }

        private void checkBoxTelegramDutyFinder_CheckedChanged(object sender, EventArgs e)
        {
            isDutyAlertEnable = checkBoxDutyFinder.Checked;
        }

        private void TTS(string text)
        {
            ActGlobals.oFormActMain.TTS(text);
        }
        private void toastWindowNotification(string text)
        {
#if COMPATIBLE
            Task.Run(() =>
            {
                MessageBox.Show(text, "ACTFate", MessageBoxButtons.OK, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button1, MessageBoxOptions.ServiceNotification);
            });
#else
            {
                try
                {
                    // Get a toast XML template
                    Windows.Data.Xml.Dom.XmlDocument toastXml = Windows.UI.Notifications.ToastNotificationManager.GetTemplateContent(Windows.UI.Notifications.ToastTemplateType.ToastImageAndText03);

                    // Fill in the text elements
                    Windows.Data.Xml.Dom.XmlNodeList stringElements = toastXml.GetElementsByTagName("text");
                    for (int i = 0; i < stringElements.Length; i++)
                    {
                        stringElements[i].AppendChild(toastXml.CreateTextNode(text));
                    }

                    // Create the toast and attach event listeners
                    Windows.UI.Notifications.ToastNotification toast = new Windows.UI.Notifications.ToastNotification(toastXml);

                    // Show the toast. Be sure to specify the AppUserModelId on your application's shortcut!
                    Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(APP_ID).Show(toast);
                }
                catch (Exception e)
                {
                    Log.Ex(e, "error");
                }
            }
#endif


        }

        private void checkBoxToastNotification_CheckedChanged(object sender, EventArgs e)
        {
            isToastNotificationEnable = checkBoxToastNotification.Checked;
        }



        private void testToastButton_Click(object sender, EventArgs e)
        {
            toastWindowNotification("Test Toast Notification");
            TTS("Test TTS");
            postToURL("Test URL Post");
        }

        private void checkBoxTTS_CheckedChanged(object sender, EventArgs e)
        {
            isTTSEnable = checkBoxTTS.Checked;
        }

        private void resetCheckedButton_Click(object sender, EventArgs e)
        {
            foreach (System.Windows.Forms.TreeNode area in this.FateTreeView.Nodes)
            {
                foreach (System.Windows.Forms.TreeNode fate in area.Nodes)
                {
                    if (fate.Checked)
                        fate.Checked = false;
                }
            }
            SelectedFates.Clear();
        }

        private void comboBoxLanguage_SelectedIndexChanged_1(object sender, EventArgs e)
        {
            selLng = comboBoxLanguage.SelectedValue.ToString();
            loadFates();
            loadBookmarks();
        }

        private void comboBoxBookmark_SelectedIndexChanged(object sender, EventArgs e)
        {
            string SelectedBookmark = comboBoxBookmark.SelectedValue.ToString();
            if(SelectedBookmark == "Bookmark")
            {
                return;
            }
            foreach (System.Windows.Forms.TreeNode area in this.FateTreeView.Nodes)
            {
                foreach (System.Windows.Forms.TreeNode fate in area.Nodes)
                {
                    foreach (var f in data["bookmarks"][SelectedBookmark]["fates"])
                    {
                        if (f.ToString().Equals(fate.Tag))
                        {
                            fate.Checked = true;
                        }
                    }
                }
            }
            updateSelectedFates(FateTreeView.Nodes);
            lockTreeEvent = false;
        }
    }

}