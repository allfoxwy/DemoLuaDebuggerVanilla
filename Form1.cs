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
using System.Diagnostics;

namespace Demo_Lua_Debugger
{
    public partial class Form1 : Form
    {
        private TcpListener conListener = null;
        private NetworkStream conStream = null;
        private BinaryReader conReader = null;
        private BinaryWriter conWriter = null;
        private Thread workerThread = null;

        const string CMD_STACK = "get stack";
        const string CMD_NEXT_TABLE_ITEM = "get next table item";
        const string CMD_LOCAL = "get local to current value";
        const string CMD_UPVALUE = "get upvalue to current value";
        const string CMD_TYPE = "get current value type";
        const string CMD_POP = "discard current value";
        const string CMD_REMOVE = "discard specific value";
        const string CMD_VALUE = "get current value as debug string";
        const string CMD_NIL = "get nil to current value";
        const string CMD_TABLE_KEY = "get table key as debug string";
        const string CMD_STACK_SHORT_SRC = "get stack item short_src";
        const string CMD_STACK_SOURCE = "get stack item source";
        const string CMD_STACK_NAME = "get stack item name";
        const string CMD_STACK_NAMEWHAT = "get stack item namewhat";
        const string CMD_STACK_CURRENTLINE = "get stack item currentline";
        const string CMD_NEXT = "next";
        const string CMD_CONTINUE = "continue till breakpoint";
      


        public Form1()
        {
            InitializeComponent();
        }

        private void conEnd()
        {
            if (conStream != null)
            {
                conStream.Close();
                conStream = null;
            }
        }
        private int recvInteger()
        {
            int v = int.MinValue;
            try
            {
                v = IPAddress.NetworkToHostOrder(conReader.ReadInt32());
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.ToString());
            }
            return v;
        }
        private void sendInteger(int v)
        {
            try
            {
                conWriter.Write(IPAddress.HostToNetworkOrder(v));
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.ToString());
            }
        }
        private string recvString()
        {
            string result = "0NULL";
            try
            {
                int len = recvInteger();
                byte[] data = conReader.ReadBytes(len);
                result = Encoding.UTF8.GetString(data);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.ToString());
            }
            return result;
        }
        private void sendString(string s)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(s);
                sendInteger(data.Length);
                conWriter.Write(data);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.ToString());
            }
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

            if (lineNumber < 0 || lineNumber >= codeTextBox.Lines.Length)
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
            if (codeTextBox.InvokeRequired)
            {
                codeTextBox.Invoke((MethodInvoker)delegate
                {
                    refreshSourceCode(src, currentLineNum);
                });
                return;
            }

            codeTextBox.Clear();

            if (src.Length < 1) { return; }

            if (src[0] != '@')
            {
                // It's not a source path, but the source itself
                codeTextBox.Text = src;
                scrollToSourceLine(currentLineNum);
                return;
            }
            else
            {
                src = src.Substring(1);
            }

            src = src.Replace('\\', Path.DirectorySeparatorChar);

            string source = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), src);

            try
            {
                StreamReader reader = new StreamReader(source, Encoding.UTF8);
                int lineCounter = 0;
                string line = reader.ReadLine();
                string total = "";
                while (line != null)
                {
                    lineCounter++;

                    total += lineCounter.ToString() + "\t";

                    total += line + "\n";

                    line = reader.ReadLine();
                }
                codeTextBox.Text = total;
                reader.Close();
                scrollToSourceLine(currentLineNum);
            }
            catch (Exception err)
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

                    sendString(CMD_STACK_SOURCE);
                    string source = recvString();

                    sendString(CMD_STACK_NAME);
                    string name = recvString();

                    sendString(CMD_STACK_NAMEWHAT);
                    string namewhat = recvString();

                    sendString(CMD_STACK_CURRENTLINE);
                    int currentline = recvInteger();

                    string stackInfo = "Call stack #" + depth + " " + src + ": " + currentline + " " + namewhat + " " + name;
                    info.Add(stackInfo);
                    Debug.WriteLine(stackInfo);

                    if (depth == 0)
                    {
                        refreshSourceCode(source, currentline);
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

        private List<TreeNode> readTableIntoTreeNodes(string nextCMD = CMD_NEXT_TABLE_ITEM)
        {
            const int max_item = 100;

            var result = new List<TreeNode>();

            sendString(CMD_NIL);
            recvInteger();

            int lua_next_result = 1;
            int item_counter = 0;
            while (lua_next_result != 0)
            {
                sendString(nextCMD);
                lua_next_result = recvInteger();

                if (lua_next_result != 0)
                {
                    sendString(CMD_TABLE_KEY);
                    string key = recvString();

                    sendString(CMD_VALUE);
                    string value = recvString();

                    TreeNode node = new TreeNode(key + " -> " + value);

                    sendString(CMD_TYPE);
                    int type = recvInteger();

                    if (type == 5)
                    {
                        // it's a table
                        TreeNode loading = new TreeNode("Loading...");
                        node.Nodes.Add(loading);

                        node.Tag = value;
                    }

                    result.Add(node);
                    item_counter++;

                    sendString(CMD_POP);
                    recvInteger();

                    if(item_counter >= max_item)
                    {
                        sendString(CMD_POP);
                        recvInteger();

                        result.Insert(0, new TreeNode("Table has more than " + max_item + " items. The rest is hidden."));
                        result.Add(new TreeNode("...the rest is hidden."));
                        return result;
                    }
                }
            }

            return result;
        }

        private bool searchTableInTable(string target, string nextCMD = CMD_NEXT_TABLE_ITEM)
        {
            Debug.WriteLine("searching table in table: " + target);

            sendString(CMD_NIL);
            recvInteger();

            int lua_next_result = 1;
            while (lua_next_result != 0)
            {
                sendString(nextCMD);
                lua_next_result = recvInteger();
                if (lua_next_result != 0)
                {
                    sendString(CMD_VALUE);
                    string value = recvString();

                    sendString(CMD_TYPE);
                    int type = recvInteger();

                    if (type == 5 && value == target)
                    {
                        // Remove table key. Leave only the value on the stack
                        sendString(CMD_REMOVE);
                        sendInteger(-2);
                        recvInteger();

                        // Found
                        Debug.WriteLine("Found " + target);
                        return true;
                    }

                    sendString(CMD_POP);
                    recvInteger();
                }
            }

            // Can't find
            return false;
        }
        
        private List<TreeNode> expandTable(TreeNode tableNode, int depth = 0)
        {
            Debug.WriteLine("expanding " + tableNode.Text);

            sendString(CMD_STACK);
            sendInteger(0);
            if(recvInteger() <= 0)
            {
                throw new Exception("Failed to select stack #0 when expand node");
            }

            if (tableNode.Parent != null)
            {
                // leaf

                expandTable(tableNode.Parent, depth + 1);
                bool searchResult = searchTableInTable((string)tableNode.Tag);
                if (false == searchResult)
                {
                    sendString(CMD_POP);
                    recvInteger();
                    throw new Exception("Can't find table " + tableNode.Text);
                }
                // remove ancestor's ancestor table
                sendString(CMD_REMOVE);
                sendInteger(-2);
                recvInteger();

                if (depth == 0)
                {
                    // the current expanded leaf
                    Debug.WriteLine("Reading " + (string)tableNode.Tag);
                    List<TreeNode> result = readTableIntoTreeNodes();
                    if(result.Count == 0)
                    {
                        result.Add(new TreeNode("Nothing."));
                    }

                    sendString(CMD_POP);
                    recvInteger();

                    return result;
                }
                else
                {
                    // ancestor leaf node
                    return null;
                }
            }
            else
            {
                // root
                if (tableNode.Text.Contains("(local)"))
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

                            sendString(CMD_TYPE);
                            int type = recvInteger();

                            if (type == 5 && localValue == (string)tableNode.Tag)
                            {
                                // We found it.
                                Debug.WriteLine("Found " + tableNode.Text);
                                break;
                            }

                            sendString(CMD_POP);
                            recvInteger();
                        }
                        localIndex++;
                    }

                    if(localName == "0NULL")
                    {
                        throw new Exception(tableNode.Text + " can not be found.");
                    }
                }
                else if(tableNode.Text.Contains("(upvalue)"))
                {
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
                            
                            sendString(CMD_TYPE);
                            int type = recvInteger();
                            if (type == 5 && upvalueValue == (string)tableNode.Tag)
                            {
                                // we found it.
                                Debug.WriteLine("Found " + tableNode.Text);
                                break;
                            }
                            sendString(CMD_POP);
                            recvInteger();
                        }
                        upvalueIndex++;
                    }

                    if (upvalueName == "0NULL")
                    {
                        throw new Exception(tableNode.Text + " can not be found.");
                    }
                }
                else
                {
                    throw new Exception(tableNode.Text + " can not be expanded. It's unknown type.");
                }

                if (depth == 0)
                {
                    // the current expanded node is the root node
                    Debug.WriteLine("Reading " + (string)tableNode.Tag);
                    List<TreeNode> result = readTableIntoTreeNodes();
                    if (result.Count == 0)
                    {
                        result.Add(new TreeNode("Nothing."));
                    }

                    sendString(CMD_POP);
                    recvInteger();

                    return result;
                } 
                else
                {
                    return null;
                }
            }
        }

        private void refreshVars()
        {
            List<TreeNode> vars = new List<TreeNode>();

            sendString(CMD_STACK);
            sendInteger(0);

            if (recvInteger() > 0)
            {
                {
                    Debug.WriteLine("Reading locals");
                    string localName = "";
                    int localCount = 0;
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
                            newNode.Tag = localValue;

                            sendString(CMD_TYPE);
                            int type = recvInteger();
                            if (type == 5)
                            {
                                // it's a table
                                TreeNode loading = new TreeNode("Loading...");
                                newNode.Nodes.Add(loading);

                                newNode.Tag = localValue;
                            }
                            vars.Add(newNode);
                            localCount++;
                            sendString(CMD_POP);
                            recvInteger();
                        }
                        localIndex++;
                    }

                    Debug.WriteLine("local count: " + localCount);
                }

                {
                    Debug.WriteLine("Reading upvalues");
                    string upvalueName = "";
                    int upvalueIndex = 1;
                    int upvalueCount = 0;
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
                            newNode.Tag = upvalueValue;

                            sendString(CMD_TYPE);
                            int type = recvInteger();
                            if (type == 5)
                            {
                                // it's a table
                                TreeNode loading = new TreeNode("Loading...");
                                newNode.Nodes.Add(loading);

                                newNode.Tag = upvalueValue;
                            }
                            vars.Add(newNode);
                            upvalueCount++;
                            sendString(CMD_POP);
                            recvInteger();
                        }
                        upvalueIndex++;
                    }
                    Debug.WriteLine("upvalue count: " +  upvalueCount);
                }
            }


            MethodInvoker refresh = delegate
            {
                varTreeView.BeginUpdate();

                varTreeView.Nodes.Clear();
                foreach (var i in vars)
                {
                    varTreeView.Nodes.Add(i);
                }

                varTreeView.EndUpdate();
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


        private void threadReadDebugEvent()
        {
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

                        MethodInvoker enableUI = delegate
                        {
                            btnNext.Enabled = true;
                            btnContinueTillBreakpoint.Enabled = true;
                            varTreeView.Enabled = true;
                        };

                        if (btnNext.InvokeRequired)
                        {
                            btnNext.Invoke(enableUI);
                        }
                        else
                        {
                            enableUI();
                        }

                        return;
                    }
                    else
                    {
                        sendString(CMD_NEXT);
                    }
                }
            }
        }
        private void threadListener()
        {
            var ip = new IPEndPoint(IPAddress.IPv6Loopback, 2323);
            conListener = new TcpListener(ip);
            try
            {
                conListener.Start();
                while (true)
                {
                    TcpClient c = conListener.AcceptTcpClient();

                    if (conStream != null)
                    {
                        c.Close();
                        continue;
                    }

                    c.NoDelay = true;
                    conStream = c.GetStream();
                    conReader = new BinaryReader(conStream);
                    conWriter = new BinaryWriter(conStream);

                    Thread worker = new Thread(threadReadDebugEvent);
                    worker.IsBackground = true;
                    worker.Start();
                }
            }
            catch
            {
                conEnd();

                if (conListener != null)
                {
                    conListener.Stop();
                    conListener = null;
                }
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            workerThread = new Thread(new ThreadStart(threadListener));
            workerThread.IsBackground = true;
            workerThread.Start();

            btnContinueTillBreakpoint.Enabled = false;
            btnNext.Enabled = false;
            varTreeView.Enabled = false;

            TextWriterTraceListener myWriter = new TextWriterTraceListener();
            myWriter.Writer = System.Console.Error;
            Debug.Listeners.Add(myWriter);

            Graphics g = this.CreateGraphics();
            float dx = 0, dy = 0;
            try
            {
                dx = g.DpiX;
                dy = g.DpiY;
            }
            finally
            {
                g.Dispose();
            }

            Debug.WriteLine("Debugger start");
            Debug.WriteLine("Window DPI at Y-axis is " + dy);

            float fontPixelSize = 0.19f * dy;
            Debug.WriteLine("Font pixel size is " + fontPixelSize);

            Font f = new Font(this.Font.Name, fontPixelSize, this.Font.Style, GraphicsUnit.Pixel);
            this.Font = f;
            btnContinueTillBreakpoint.Font = f;
            btnNext.Font = f;
            varTreeView.Font = f;
            callStackListBox.Font = f;
            codeTextBox.Font = f;
            this.Refresh();
        }

        private void btnNext_Click(object sender, EventArgs e)
        {
            btnContinueTillBreakpoint.Enabled = false;
            btnNext.Enabled = false;
            varTreeView.Enabled = false;

            sendString(CMD_NEXT);

            Thread worker = new Thread(threadReadDebugEvent);
            worker.IsBackground = true;
            worker.Start();
        }

        private void btnContinueTillBreakpoint_Click(object sender, EventArgs e)
        {
            btnContinueTillBreakpoint.Enabled = false;
            btnNext.Enabled = false;
            varTreeView.Enabled = false;

            sendString(CMD_CONTINUE);

            Thread worker = new Thread(threadReadDebugEvent);
            worker.IsBackground = true;
            worker.Start();
        }

        

        private void varTreeView_AfterExpand(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Nodes[0] != null && e.Node.Nodes[0].Text.Contains("Loading"))
            {
                varTreeView.BeginUpdate();
                varTreeView.Enabled = false;
                btnContinueTillBreakpoint.Enabled = false;
                btnNext.Enabled = false;

                List<TreeNode> result = expandTable(e.Node);
                e.Node.Nodes.Clear();
                foreach (TreeNode node in result)
                {
                    e.Node.Nodes.Add(node);
                }

                varTreeView.Enabled = true;
                btnContinueTillBreakpoint.Enabled = true;
                btnNext.Enabled = true;
                varTreeView.EndUpdate();
            }
        }
    }
}
