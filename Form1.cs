using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using System.Collections;
using System.Xml.Schema;
using System.Runtime.InteropServices;

namespace Demo_Lua_Debugger
{
    public partial class Form1 : Form
    {
        private TcpListener conListener = null;
        private NetworkStream conStream = null;
        private BinaryReader conReader = null;
        private BinaryWriter conWriter = null;
        private Thread workerThread = null;

        private HashSet<string> knownTables = null;

        const string CMD_STACK = "get stack";
        const string CMD_NEXT_TABLE_ITEM = "get next table item";
        const string CMD_LOCAL = "get local to current value";
        const string CMD_UPVALUE = "get upvalue to current value";
        const string CMD_TYPE = "get current value type";
        const string CMD_DISCARD = "discard current value";
        const string CMD_VALUE = "get current value as debug string";
        const string CMD_NIL = "get nil to current value";
        const string CMD_TABLE_KEY = "get table key as debug string";
        const string CMD_STACK_SHORT_SRC = "get stack item short_src";
        const string CMD_STACK_NAME = "get stack item name";
        const string CMD_STACK_NAMEWHAT = "get stack item namewhat";
        const string CMD_STACK_CURRENTLINE = "get stack item currentline";
        const string CMD_NEXT = "next";
        const string CMD_CONTINUE = "continue till breakpoint";
      


        public Form1()
        {
            InitializeComponent();
        }
        private int recvInteger()
        {
            return IPAddress.NetworkToHostOrder(conReader.ReadInt32());
        }
        private void sendInteger(int v)
        {
            conWriter.Write(IPAddress.HostToNetworkOrder(v));
        }
        private string recvString()
        {
            int len = recvInteger();
            byte[] data = conReader.ReadBytes(len);
            return Encoding.UTF8.GetString(data);
        }
        private void sendString(string s)
        {
            byte[] data = Encoding.UTF8.GetBytes(s);
            sendInteger(data.Length);
            conWriter.Write(data);
        }
        
        private void scrollToSourceLine(int lineNumber)
        {
            if(codeTextBox.InvokeRequired)
            {
                codeTextBox.Invoke((MethodInvoker)delegate
                {
                    scrollToSourceLine(lineNumber);
                });
            }

            lineNumber -= 1;

            if(lineNumber < 0 || lineNumber > codeTextBox.Lines.Length)
            {
                return;
            }

            codeTextBox.Select(codeTextBox.GetFirstCharIndexFromLine(lineNumber), codeTextBox.Lines[lineNumber].Length);
            codeTextBox.SelectionBackColor = Color.SkyBlue;

            codeTextBox.Select(codeTextBox.GetFirstCharIndexFromLine(lineNumber), 0);

            codeTextBox.ScrollToCaret();
        }
        private void refreshSourceCode(string src, int currentLineNum)
        {
            if(codeTextBox.InvokeRequired)
            {
                codeTextBox.Invoke((MethodInvoker)delegate {
                    refreshSourceCode(src, currentLineNum);
                });
                return;
            }


            src = src.Replace('\\', Path.DirectorySeparatorChar);

            string source = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), src);

            try
            {
                StreamReader reader = new StreamReader(source, Encoding.UTF8);
                int lineCounter = 0;
                string line = reader.ReadLine();
                string total = "";
                while(line != null)
                {
                    lineCounter++;

                    total += lineCounter.ToString() + "\t";

                    total += line + "\n";
                    
                    line = reader.ReadLine();
                }
                codeTextBox.Clear();
                codeTextBox.Text = total;
                reader.Close();
                scrollToSourceLine(currentLineNum);
            }
            catch(Exception err)
            {
                codeTextBox.Text = "No source preview for " + source + "\n" + err.ToString();

            }

        }
        private void refreshCallStackInfo()
        {
            List<string> info = new List<string>();
            int result = 1;
            int depth = 0;

            while (result > 0)
            {
                sendString(CMD_STACK);
                sendInteger(depth);

                result = recvInteger();
                if (result > 0)
                {
                    sendString(CMD_STACK_SHORT_SRC);
                    string src = recvString();

                    sendString(CMD_STACK_NAME);
                    string name = recvString();

                    sendString(CMD_STACK_NAMEWHAT);
                    string namewhat = recvString();

                    sendString(CMD_STACK_CURRENTLINE);
                    int currentline = recvInteger();

                    string stackInfo = "Call stack #" + depth + " " + src + ": " + currentline + " " + namewhat + " " + name;
                    info.Add(stackInfo);

                    if(depth == 0)
                    {
                        refreshSourceCode(src, currentline);
                    }
                }
                ++depth;
            }

            MethodInvoker refresh = delegate
            {
                callStackListBox.Items.Clear();
                foreach (var i in info)
                {
                    callStackListBox.Items.Add(i);
                }
            };

            if (callStackListBox.InvokeRequired)
            {
                callStackListBox.Invoke(refresh);
            }
            else
            {
                refresh();
            }
        }

        List<TreeNode> iterateOverTable(int depth)
        {
            List<TreeNode> result = new List<TreeNode>();
            if (depth > 2)
            {
                result.Add(new TreeNode("...and more"));
                return result;
            }

            sendString(CMD_NIL);
            recvInteger();

            int counter = 0;
            int next = 1;
            const int max = 10;
            while (next != 0 && counter < max)
            {
                sendString(CMD_NEXT_TABLE_ITEM);
                next = recvInteger();
                if (next != 0)
                {
                    ++counter;

                    sendString(CMD_TABLE_KEY);
                    string key = recvString();

                    sendString(CMD_VALUE);
                    string value = recvString();

                    TreeNode newNode = new TreeNode(key + " => " + value);

                    sendString(CMD_TYPE);
                    int type = recvInteger();
                    if (type == 5)
                    {
                        // it's another table
                        if (false == knownTables.Contains(value))
                        {
                            knownTables.Add(value);
                            List<TreeNode> tree = iterateOverTable(depth + 1);
                            foreach (var i in tree)
                            {
                                newNode.Nodes.Add(i);
                            }
                        }
                    }
                    result.Add(newNode);
                    sendString(CMD_DISCARD);
                    recvInteger();
                }
            }

            if (next != 0 && counter >= max)
            {
                sendString(CMD_DISCARD);
                recvInteger();
            }

            if (result.Count == 0)
            {
                result.Add(new TreeNode("nothing"));
                return result;
            }
            else
            {
                return result;
            }
        }
        private void refreshVars()
        {
            List<TreeNode> vars = new List<TreeNode>();

            sendString(CMD_STACK);
            sendInteger(0);

            if (recvInteger() > 0)
            {
                knownTables = new HashSet<string>();

                {
                    string localName = "";
                    int localIndex = 1;
                    while (localName != "0NULL")
                    {
                        sendString(CMD_LOCAL);
                        sendInteger(localIndex);

                        localName = recvString();
                        if (localName != "0NULL")
                        {
                            sendString(CMD_VALUE);
                            string localValue = recvString();
                            TreeNode newNode = new TreeNode("(local)" + localName + " = " + localValue);

                            sendString(CMD_TYPE);
                            int type = recvInteger();
                            if (type == 5)
                            {
                                // it's a table
                                if (false == knownTables.Contains(localName))
                                {
                                    knownTables.Add(localName);
                                    List<TreeNode> tree = iterateOverTable(1);
                                    foreach (var i in tree)
                                    {
                                        newNode.Nodes.Add(i);
                                    }
                                }
                            }
                            vars.Add(newNode);
                            sendString(CMD_DISCARD);
                            recvInteger();
                        }
                        localIndex++;
                    }
                }

                {
                    knownTables.Clear();
                    string upvalueName = "";
                    int upvalueIndex = 1;
                    while (upvalueName != "0NULL")
                    {
                        sendString(CMD_UPVALUE);
                        sendInteger(upvalueIndex);

                        upvalueName = recvString();
                        if (upvalueName != "0NULL")
                        {
                            sendString(CMD_VALUE);
                            string upvalueValue = recvString();
                            TreeNode newNode = new TreeNode("(upvalue)" + upvalueName + " = " + upvalueValue);

                            sendString(CMD_TYPE);
                            int type = recvInteger();
                            if (type == 5)
                            {
                                // it's a table
                                if (false == knownTables.Contains(upvalueName))
                                {
                                    knownTables.Add(upvalueName);
                                    List<TreeNode> tree = iterateOverTable(1);
                                    foreach (var i in tree)
                                    {
                                        newNode.Nodes.Add(i);
                                    }
                                }
                            }
                            vars.Add(newNode);
                            sendString(CMD_DISCARD);
                            recvInteger();
                        }
                        upvalueIndex++;
                    }
                }
            }

            MethodInvoker refresh = delegate
            {
                varTreeView.Nodes.Clear();
                foreach (var i in vars)
                {
                    varTreeView.Nodes.Add(i);
                }
            };

            if (varTreeView.InvokeRequired)
            {
                varTreeView.Invoke(refresh);
            }
            else
            {
                refresh();
            }
        }
        private void threadWorker()
        {
            var ip = new IPEndPoint(IPAddress.IPv6Loopback, 2323);
            conListener = new TcpListener(ip);
            try
            {
                conListener.Start();
                while (true)
                {
                    TcpClient c = conListener.AcceptTcpClient();
                    c.NoDelay = true;
                    conStream = c.GetStream();
                    conReader = new BinaryReader(conStream);
                    conWriter = new BinaryWriter(conStream);
                    while (true)
                    {
                        string cmd = recvString();

                        if (cmd == "debug event")
                        {
                            int debugEventType = recvInteger();
                            // We only process line event as demo
                            if (debugEventType == 2)
                            {
                                refreshCallStackInfo();
                                refreshVars();

                                btnNext.Enabled = true;
                                btnContinueTillBreakpoint.Enabled = true;
                            }
                            else
                            {
                                sendString(CMD_NEXT);
                            }
                        }
                    }
                }
            }
            catch
            {
                if(conStream != null)
                {
                    conStream.Close();
                }
                if(conListener!=null)
                {
                    conListener.Stop();
                }
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            workerThread = new Thread(new ThreadStart(threadWorker));
            workerThread.IsBackground = true;
            workerThread.Start();

            btnContinueTillBreakpoint.Enabled = false;
            btnNext.Enabled = false;
        }

        private void btnNext_Click(object sender, EventArgs e)
        {
            btnContinueTillBreakpoint.Enabled = false;
            btnNext.Enabled = false;

            sendString(CMD_NEXT);
        }

        private void btnContinueTillBreakpoint_Click(object sender, EventArgs e)
        {
            btnContinueTillBreakpoint.Enabled = false;
            btnNext.Enabled = false;

            sendString(CMD_CONTINUE);
        }

    }
}
