using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Media;
using Process.NET.Native.Types;
using Process.NET;
using System.Diagnostics;
using System.Threading;

namespace DBFZRecordingAudioCues
{
    public partial class Form1 : Form
    {
        const string MAGIC_WORD = "DBFZREC ";
        string ext = "dbzrec";
        ExternalProcessMemory dbfzmem;
        List<SoundPlayer> spList;
        IntPtr framecounter;
        IntPtr recordingStart;
        IntPtr inputbuffer;
        const int PLAYBACK_MASK = 0x800;
        List<int> LMHSList = new List<int> { 0x10, 0x20, 0x40, 0x80, 0x100, 0x200 };
        CancellationTokenSource cts = new CancellationTokenSource();
        CancellationToken tok;
        short[] recording;
        public Form1()
        {
            initSound();
            initMemory(); 
            InitializeComponent();
        }

        private void initSound()
        {
            var lsp = new SoundPlayer(@"sounds\dbfzblipL.wav");
            var msp = new SoundPlayer(@"sounds\dbfzblipM.wav");
            var hsp = new SoundPlayer(@"sounds\dbfzblipH.wav");
            var ssp = new SoundPlayer(@"sounds\dbfzblipS.wav");
            var a1sp = new SoundPlayer(@"sounds\dbfzblipA1.wav");
            var a2sp = new SoundPlayer(@"sounds\dbfzblipA2.wav");
            spList = new List<SoundPlayer> { lsp, msp, hsp, ssp, a1sp, a2sp };
            foreach (SoundPlayer spl in spList)
            {
                spl.Load();
            }
        }
        private void initMemory(){
            System.Diagnostics.Process dbfz = System.Diagnostics.Process.GetProcessesByName("RED-Win64-Shipping")[0];
            var dbfzproc = new ProcessSharp(dbfz);
            dbfzmem = new ExternalProcessMemory(dbfzproc.Handle);
            var baseaddress = dbfzproc.ModuleFactory.MainModule.BaseAddress;
            framecounter = IntPtr.Add(baseaddress, 0x35EAB48);
            var recordingptr = (IntPtr)dbfzmem.Read<int>(IntPtr.Add(baseaddress, 0x3817BD8));
            recordingStart = IntPtr.Add(recordingptr, 0x718);
            inputbuffer = IntPtr.Add(baseaddress, 0x35EAC08);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (button1.Text.StartsWith("Enable"))
            {
                tok = cts.Token;
                Task mainLoop = new Task(() => detectPlayback());
                var reclength = dbfzmem.Read<int>(IntPtr.Add(recordingStart, 4));
                recording = new short[reclength];
                for (var i = 0; i < reclength; i++)
                {
                    recording[i] = dbfzmem.Read<short>(IntPtr.Add(recordingStart, 8 + 2 * i));
                }
                mainLoop.Start();
                button1.Text = "Disable Audio Cues";
            }
            else
            {
                cts.Cancel();
                cts = new CancellationTokenSource();
                button1.Text = "Enable Audio Cues";
            }
        }

        private void detectPlayback()
        {
            while (!tok.IsCancellationRequested)
            {
                if ((dbfzmem.Read<int>(inputbuffer) & PLAYBACK_MASK) != PLAYBACK_MASK)
                {
                    waitFrames(1);
                }
                else
                {
                    waitFrames(1);
                    short input = recording[0];
                    for(var i = 0; i < LMHSList.Count; i++)
                    {
                        if((input & LMHSList[i]) == LMHSList[i])
                        {
                            spList[i].Play();
                        }
                    }
                    short oldinput = input;
                    bool first = true;
                    foreach (short currentinput in recording)
                    {
                        if (first)
                        {
                            first = !first;
                            continue;
                        }
                        for (var i = 0; i < LMHSList.Count; i++)
                        {
                            if((oldinput & LMHSList[i]) != LMHSList[i] && (currentinput & LMHSList[i]) == LMHSList[i])
                            {
                                spList[i].Play();
                            }
                        }
                        oldinput = currentinput;
                        waitFrames(1);
                    }
                }
            }
        }
        private void waitFrames(int frames)
        {
            var framec = dbfzmem.Read<int>(framecounter);
            while(!tok.IsCancellationRequested && dbfzmem.Read<int>(framecounter) < frames + framec)
            {

            }
        }

        private void loadbutton_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = "Load Recording";
            ofd.Filter = "DBZREC Files (*." + ext + ")|*." + ext;
            ofd.AddExtension = true;
            ofd.DefaultExt = ext;
            ofd.CheckFileExists = true;
            ofd.CheckPathExists = true;
            DialogResult = ofd.ShowDialog();
            if (DialogResult == DialogResult.OK)
            {
                var fileStream = ofd.OpenFile();
                byte[] buf = new byte[8];
                fileStream.Read(buf, 0, 8);
                if (Encoding.ASCII.GetString(buf) != MAGIC_WORD)
                {
                    MessageBox.Show("Invalid format for DBZREC file.");
                    fileStream.Close();
                    return;
                }
                var numBytes = fileStream.Length - 8;
                fileStream.Seek(8, System.IO.SeekOrigin.Begin);
                for(var i = 0; i < numBytes; i++)
                {
                    dbfzmem.Write(IntPtr.Add(recordingStart, i), fileStream.ReadByte());
                }
                fileStream.Close();
            }
        }

        private void savebutton_Click(object sender, EventArgs e)
        {

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.CreatePrompt = true;
            sfd.OverwritePrompt = true;
            sfd.FileName = "MyRecording";
            sfd.Title = "Save Current Recording";
            sfd.DefaultExt = ext;
            sfd.Filter = "DBZREC Files (*." + ext + ")|*." + ext;
            DialogResult result = sfd.ShowDialog();
            if (result == DialogResult.OK)
            {
                var fileStream = sfd.OpenFile();
                fileStream.Write(Encoding.ASCII.GetBytes(MAGIC_WORD), 0, MAGIC_WORD.Length);
                int recLength = dbfzmem.Read<int>(IntPtr.Add(recordingStart, 4)) * 2 + 8;
                for (int i = 0; i < recLength; i++)
                {
                    fileStream.WriteByte(dbfzmem.Read<Byte>(IntPtr.Add(recordingStart, i)));
                }
                fileStream.Close();
            }
        }
    }
}
