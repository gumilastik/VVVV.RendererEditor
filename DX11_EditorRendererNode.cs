#region usings

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Device = SlimDX.Direct3D11.Device;
using SlimDX.DXGI;
using SlimDX.Direct3D11;
using SlimDX;

using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VMath;
using VVVV.Utils.VColor;
using VVVV.PluginInterfaces.V1;

using VVVV.Utils.IO;
using VVVV.Core.Logging;
using VVVV.PluginInterfaces.V2.Graph;

using System.Diagnostics;
using System.Drawing;
using System.ComponentModel.Composition;
using System.Windows.Forms;
using System.Runtime.InteropServices;

using FeralTic.DX11.Queries;
using FeralTic.DX11;
using FeralTic.DX11.Resources;

using VVVV.DX11.Lib.Devices;
using VVVV.DX11.Lib.Rendering;
using VVVV.DX11.Nodes.Renderers.Graphics.Touch;

#endregion usings


using System.IO;
using System.Reflection;

using VVVV.Hosting;
using VVVV.Hosting.Interfaces;
using VVVV.Hosting.IO;

using VVVV.DX11;
using VVVV.DX11.RenderGraph.Model;

using System.Windows.Forms.Integration;


namespace VVVV.DX11.Nodes
{
    [PluginInfo(Name = "Renderer", Category = "DX11.Editor", Version = "", Author = "", AutoEvaluate = true,
        InitialWindowHeight = 300, InitialWindowWidth = 400, InitialBoxWidth = 400, InitialBoxHeight = 300, InitialComponentMode = TComponentMode.InAWindow)]
    public partial class DX11_EditorRendererNode : UserControl, IPluginEvaluate, IDisposable, IDX11RendererProvider, IDX11RenderWindow, IDX11Queryable, IUserInputWindow, IBackgroundColor
    {
        [Import()]
        public ILogger FLogger;

        private DX11StagingTexture2D rgbaTex;
        private DX11RenderTarget2D renderTarget;

        private DX11StagingTexture2D depthTex;

        private EditorControls.Toolbar editorToolbar;
        private ElementHost host;

        void AfterInitializeComponent()
        {
            editorToolbar = new EditorControls.Toolbar();


            host = new ElementHost();
            host.Dock = DockStyle.None;
            host.AutoSize = true;
            host.Child = editorToolbar;

            this.Controls.Add(host);

            host.Child.PreviewMouseDown += UserControl_MouseDown;
            host.Child.PreviewMouseMove += UserControl_MouseMove;
            host.Child.PreviewMouseUp += UserControl_MouseUp;
 
            this.MouseMove += Form_MouseMove;
            this.MouseUp += Form_MouseUp;


        }

        private int xPos, yPos; // mouse coord
        private bool drag;

        private void UserControl_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {

            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
               drag = true;

                // ((Control)sender).PointToScreen

                xPos = (int)e.GetPosition(sender as EditorControls.Toolbar).X;
                yPos = (int)e.GetPosition(sender as EditorControls.Toolbar).Y;
                // 		FLogger.Log(LogType.Debug, "x " + xPos + ", sx = " + screenPoint);
            }
        }

        private void UserControl_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
          if (drag == true && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                host.Location = new Point(host.Location.X + (int)e.GetPosition(sender as EditorControls.Toolbar).X - xPos, host.Location.Y + (int)e.GetPosition(sender as EditorControls.Toolbar).Y - yPos);
            }
        }


        private void Form_MouseMove(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (drag == true && e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                host.Location = new Point(e.X - xPos, e.Y - yPos);
            }
        }


        private void UserControl_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Released)
            {
                drag = false;
            }
        }

        private void Form_MouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
                drag = false;
        }





        private T GetPrivateFieldValue<T>(string propName)
        {
            FieldInfo field = typeof(DX11_EditorRendererNode).GetField(propName, BindingFlags.Instance | BindingFlags.NonPublic);
            return (T)field.GetValue(this as DX11_EditorRendererNode);
        }

        class NodeWithPins
        {
            public DX11Node node;
            public List<DX11InputPin> pinLayer;
            //            public List<DX11InputPin> pinTransform;
        }

        int nNode = -1; // selected node
        string nodePath; // use it for test existing node
        Vector2D mousePos;
        float minZ = 1;
        List<NodeWithPins> nodes = new List<NodeWithPins>();

        // find tree for renderer
        public void RecursiveSearch(List<DX11Node> list, DX11Node node)
        {
            // fast test for disabled group
            object plugNode = ((PluginContainer)((IInternalPluginHost)node.Hoster).Plugin).PluginBase;
            if (plugNode is DX11LayerGroupNode && (plugNode as DX11LayerGroupNode).Enabled == false) return;


            if (plugNode is AbstractDX11Renderer2DNode) { FLogger.Log(LogType.Debug, "tree -> " + plugNode); return; }

            List<DX11Node> _list = new List<DX11Node>();

            NodeWithPins nodeWithPins = new NodeWithPins();
            nodeWithPins.node = node;
            nodeWithPins.pinLayer = new List<DX11InputPin>();

            foreach (DX11InputPin pin in node.InputPins) //pins)
            {
                if (pin.ParentPin != null)
                {

                    INode2 n2 = hde.GetNodeFromPath(node.HdeNode.GetNodePath(false));
                    if (list.Contains(pin.ParentPin.ParentNode))
                    {
                        nodeWithPins.pinLayer.Add(pin);

                        _list.Add(pin.ParentPin.ParentNode);
                    }
                }
            }

            nodes.Add(nodeWithPins);
            foreach (DX11Node _node in _list)
            {
                //FLogger.Log(LogType.Debug, " -> " + _node.Name);
                RecursiveSearch(list, _node);
            }
        }


        public void AfterRender(DX11RenderContext context)
        {
            if (this.FOutMouseState[0].IsMiddle)
            {
                string path;
                this.FHost.GetNodePath(false, out path);
                INode2 node = hde.GetNodeFromPath(path);
                //FLogger.Log(LogType.Debug, "id: " + node.ID);

                DX11Graph graph = DX11GlobalDevice.RenderManager.RenderGraphs[context].Graph;

                for (int i = 0; i < graph.Nodes.Count; i++)
                {
                    if ((graph.Nodes[i].HdeNode as INode).Equals(node.InternalCOMInterf))
                    {
                        nodes.Clear();
                        RecursiveSearch(graph.Nodes, graph.Nodes[i]);
                        //foreach (NodeWithPins n in nodes) FLogger.Log(LogType.Debug, "0---> "+n.node.Name + " , "+n.node.HdeNode.GetID()); 
                        break;
                    }
                }




                if (nodes.Count > 0) // FInLayer.IsConnected && 
                {
                    Dictionary<DX11RenderContext, DX11GraphicsRenderer> renderers = GetPrivateFieldValue<Dictionary<DX11RenderContext, DX11GraphicsRenderer>>("renderers");
                    DX11GraphicsRenderer renderer = renderers[context];

                    DX11RenderSettings settings = GetPrivateFieldValue<DX11RenderSettings>("settings");

                    settings.ViewportIndex = 0;
                    settings.View = this.FInView[0];
                    Matrix proj = this.FInProjection[0];
                    Matrix aspect = Matrix.Invert(this.FInAspect[0]);
                    Matrix crop = Matrix.Invert(this.FInCrop[0]);
                    settings.Projection = proj * aspect * crop;
                    settings.ViewProjection = settings.View * settings.Projection;
                    settings.BackBuffer = this.FOutBackBuffer[0][context];
                    settings.RenderWidth = this.FOutBackBuffer[0][context].Resource.Description.Width;
                    settings.RenderHeight = this.FOutBackBuffer[0][context].Resource.Description.Height;
                    settings.ResourceSemantics.Clear();
                    settings.CustomSemantics.Clear();

                    SampleDescription sd = new SampleDescription(1, 0);

                    depthTex = new DX11StagingTexture2D(context, settings.RenderWidth, settings.RenderHeight, Format.D32_Float);

                    rgbaTex = new DX11StagingTexture2D(context, settings.RenderWidth, settings.RenderHeight, SlimDX.DXGI.Format.R8G8B8A8_UNorm);
                    renderTarget = new DX11RenderTarget2D(context, settings.RenderWidth, settings.RenderHeight, sd, Format.R8G8B8A8_UNorm, false, 0);

                    Device device = context.Device;
                    DX11SwapChain chain = this.FOutBackBuffer[0][context];
                    renderer.EnableDepth = this.FInDepthBuffer[0];


                    renderer.SetRenderTargets(renderTarget);
                    //renderer.SetRenderTargets(chain);
                    renderer.SetTargets();

                    bool enableDepth = renderer.DepthMode != eDepthBufferMode.None;

                    long xpos, ypos, dpos, cpos;

                    xpos = (long)((this.FOutMouseState[0].X + 1) / 2 * settings.RenderWidth);
                    ypos = (long)((settings.RenderHeight * (1 - this.FOutMouseState[0].Y) / 2) * (depthTex.GetRowPitch() / sizeof(float)));//settings.RenderWidth ) ;
                    dpos = sizeof(float) * (ypos + xpos);


                    xpos = (long)((this.FOutMouseState[0].X + 1) / 2 * settings.RenderWidth);
                    ypos = (long)((settings.RenderHeight * (1 - this.FOutMouseState[0].Y) / 2) * (rgbaTex.GetRowPitch() / (4 * sizeof(byte))));
                    cpos = (4 * sizeof(byte)) * (ypos + xpos);




                    float z = minZ;
                    nNode = -1; // no selected node
                    for (int i = 1; i < nodes.Count; i++) // skip current Render
                    {
                        IPluginHost host = nodes[i].node.Hoster;
                        if (host != null)
                        {
                            IInternalPluginHost iip = (IInternalPluginHost)host;
                            if (iip.Plugin is PluginContainer)
                            {
                                PluginContainer plugin = (PluginContainer)iip.Plugin;
                                object plugNode = plugin.PluginBase;
                                if (!(plugNode is DX11LayerGroupNode)) // skip DX11LayerGroupNode
                                {
                                    //FLogger.Log(LogType.Debug, "plugNode: " + plugNode.GetType() );

                                    MethodInfo dynMethodRender;
                                    dynMethodRender = plugNode.GetType().GetMethod("Render", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (dynMethodRender != null)
                                    {
                                        //FLogger.Log(LogType.Debug, "i: " + i + plugNode );

                                        if (enableDepth) renderer.DepthStencil.Clear(true, true, minZ, 0);
                                        renderer.Clear(new SlimDX.Color4(0.0f, 0.0f, 0.0f, 0.0f)); // argb

                                        dynMethodRender.Invoke(plugNode, new object[] { this.FInLayer.PluginIO, context, settings });

                                        float _z = 0;

                                        // depth
                                        if (enableDepth)
                                        {
                                            depthTex.CopyFrom(renderer.DepthStencil.Stencil);
                                            DataBox db = depthTex.LockForRead();
                                            db.Data.Position = dpos;

                                            _z = db.Data.Read<float>();
                                            //        FLogger.Log(LogType.Debug, "z: " + z);//.ReadByte());
                                            depthTex.UnLock();
                                        }

                                        //col
                                        rgbaTex.CopyFrom(renderTarget);
                                        DataBox db2 = rgbaTex.LockForRead();
                                        db2.Data.Position = cpos;

                                        byte[] col = db2.Data.ReadRange<byte>(4);
                                        //FLogger.Log(LogType.Debug, "color!: " + col[0] + ","  + col[1] + ","  + col[2] + "," + col[3]);
                                        rgbaTex.UnLock();

                                        if ((enableDepth ? _z <= z : true) && col[3] > 0)
                                        {
                                            //FLogger.Log(LogType.Debug, "i: " + i + " , _z = " + _z + " <= z = " + z); 
                                            z = _z;
                                            nNode = i;

                                        }

                                    }


                                }

                            }

                        }

                    }

                    context.RenderStateStack.Reset();
                    renderer.CleanTargets();

                    // find node
                    if (nNode >= 0)
                    {
                        nodePath = nodes[nNode].node.HdeNode.GetNodePath(false);

                        INode2 node2 = (INode2)hde.GetNodeFromPath(nodePath);
                        hde.ShowEditor(node2.Parent);
                        hde.SelectNodes(new INode2[1] { node2 });

                        this.Focus();
                    }

                    //else hdehost.SelectNodes(new INode2[0] { });


                    depthTex.Dispose();
                    //depthStencil.Dispose();

                    rgbaTex.Dispose();
                    renderTarget.Dispose();
                }

            }



        }


        public void AfterEvaluate()
        {
            Vector2D mousePosNew = FMousePos;//new Vector2D(FOutMouseState[0].X, FOutMouseState[0].Y);

            if (FOutMouseState[0].IsRight && nNode >= 0)
            {
                INode2 node2 = (INode2)hde.GetNodeFromPath(nodePath);
                IPin2[] pins = node2.Pins.ToArray();

                // find first transform pin
                // or use .FindPin("Transform") .FindPin("Transform In")
                int j = -1;
                for (int i = 0; i < pins.Count(); i++)
                {
                    if (pins[i].Direction == PinDirection.Input && pins[i].Type.Equals("-> Render Transform")) { j = i; break; }
                    //FLogger.Log(LogType.Debug, pins[i].InternalCOMInterf.GetSlice(0) + " " + pins[i].Type + " " + pins[i].Name);
                }

                if (j >= 0)
                {
                    bool isOtherTransform = false;
                    bool isOtherTransformEmpty = false;

                    INode2 otherNode2 = null;

                    if (pins[j].IsConnected())  // already connected with transform node, then test node for Name and pins
                    {
                        isOtherTransform = true;

                        otherNode2 = pins[j].ConnectedPins.First().ParentNode;

                        if (otherNode2.NodeInfo.Systemname.Equals("Transform (Transform 3d)"))
                        {
                            isOtherTransformEmpty = true;
                            IPin2[] pins2 = otherNode2.Pins.ToArray();
                            for (int i = 0; i < pins2.Count(); i++)
                            {
                                if (pins2[i].Direction == PinDirection.Input && pins2[i].Type.Equals("Value") && pins2[i].IsConnected()) { isOtherTransformEmpty = false; break; }
                            }
                        }
                    }

                    //FLogger.Log(LogType.Debug, "isOtherTransform: " + isOtherTransform + " , isOtherTransformEmpty: " + isOtherTransformEmpty);
                    string patchPath = hde.ActivePatchWindow.Node.NodeInfo.Filename;
                    PatchMessage patch = new PatchMessage("");
                    if (isOtherTransform && isOtherTransformEmpty)
                    {
                        NodeMessage nodeMessage = patch.AddNode(otherNode2.ID);

                        PinMessage pinMessage;

                        double xDiff = (mousePosNew - mousePos).x;
                        double yDiff = (mousePosNew - mousePos).y;
                        double len = (xDiff + yDiff) / 2;



                        if (editorToolbar.IsX())
                        {
                            string pinName = editorToolbar.IsMove() ? "TranslateX" : (editorToolbar.IsScale() ? "ScaleX" : "Pitch");

                            double val = double.Parse(otherNode2.FindPin(pinName).Spread, System.Globalization.CultureInfo.InvariantCulture);
                            val += !editorToolbar.IsUniform() ? xDiff : len;

                            pinMessage = new PinMessage(nodeMessage.OwnerDocument, pinName);
                            pinMessage.Spread = val.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            nodeMessage.AppendChild(pinMessage);
                        }
                        if (editorToolbar.IsY())
                        {
                            string pinName = editorToolbar.IsMove() ? "TranslateY" : (editorToolbar.IsScale() ? "ScaleY" : "Yaw");

                            double val = double.Parse(otherNode2.FindPin(pinName).Spread, System.Globalization.CultureInfo.InvariantCulture);
                            val += !editorToolbar.IsUniform() ? yDiff : len;

                            pinMessage = new PinMessage(nodeMessage.OwnerDocument, pinName);
                            pinMessage.Spread = val.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            nodeMessage.AppendChild(pinMessage);
                        }
                        if (editorToolbar.IsZ())
                        {
                            string pinName = editorToolbar.IsMove() ? "TranslateZ" : (editorToolbar.IsScale() ? "ScaleZ" : "Roll");

                            double val = double.Parse(otherNode2.FindPin(pinName).Spread, System.Globalization.CultureInfo.InvariantCulture);
                            val += !editorToolbar.IsUniform() ? (!editorToolbar.IsX() ? xDiff : yDiff) : len;

                            pinMessage = new PinMessage(nodeMessage.OwnerDocument, pinName);
                            pinMessage.Spread = val.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            nodeMessage.AppendChild(pinMessage);
                        }



                    }
                    else
                    {
                        NodeMessage nodeMessage = patch.AddNode("Transform (Transform 3d)");
                        //PinMessage pinMessage = new PinMessage(nodeMessage.OwnerDocument, "TranslateX");
                        //pinMessage.Spread = "0.123";
                        //nodeMessage.AppendChild(pinMessage);

                        if (isOtherTransform) patch.AddLink(otherNode2.ID, "Transform Out", -1, "Transform In");
                        patch.AddLink(-1, "Transform Out", node2.ID, pins[j].Name);

                        Rectangle nodeBounds = node2.GetBounds(BoundsType.Node);
                        nodeBounds.Y -= 350;
                        nodeBounds.X += 170;
                        nodeBounds.Width = 100;
                        nodeBounds.Height = 100;

                        BoundsMessage boundsMessage = new BoundsMessage(nodeMessage.OwnerDocument, BoundsType.Node);
                        boundsMessage.Rectangle = nodeBounds;
                        nodeMessage.AppendChild(boundsMessage);

                    }
                    hde.SendXMLSnippet(patchPath, patch.ToString(), true);
                }
            }

            mousePos = mousePosNew;
        }

        void DrawSelectedNode(DX11RenderContext context)
        {
            // hack select node
            if (nNode >= 0)
            {
                INode2 node2 = (INode2)hde.GetNodeFromPath(nodePath);

                if (node2 != null && !node2.HasPatch)
                {
                    object plugNode = ((PluginContainer)((IInternalPluginHost)nodes[nNode].node.Hoster).Plugin).PluginBase;
                    MethodInfo dynMethodRender = plugNode.GetType().GetMethod("Render", BindingFlags.Public | BindingFlags.Instance);
                    if (dynMethodRender != null)
                    {
                        var rss = new DX11RenderState();
                        rss.Rasterizer = new RasterizerStateDescription()
                        {
                            CullMode = CullMode.None,
                            DepthBias = 0,
                            DepthBiasClamp = 0.0f,
                            FillMode = FillMode.Wireframe,
                            IsAntialiasedLineEnabled = false,
                            IsDepthClipEnabled = false,
                            IsFrontCounterclockwise = false,
                            IsMultisampleEnabled = false,
                            IsScissorEnabled = false,
                            SlopeScaledDepthBias = 0.0f
                        };

                        rss.Blend = DX11BlendStates.Instance.GetState("Blend");
                        rss.Blend.RenderTargets[0] =
                        new RenderTargetBlendDescription()
                        {
                            BlendEnable = true,
                            BlendOperation = BlendOperation.Subtract,
                            BlendOperationAlpha = BlendOperation.Maximum,
                            DestinationBlend = BlendOption.InverseDestinationColor,
                            DestinationBlendAlpha = BlendOption.Zero,
                            RenderTargetWriteMask = ColorWriteMaskFlags.All,
                            SourceBlend = BlendOption.One,
                            SourceBlendAlpha = BlendOption.One
                        };

                        context.RenderStateStack.Push(rss);
                        dynMethodRender.Invoke(plugNode, new object[] { this.FInLayer.PluginIO, context, settings });
                        context.RenderStateStack.Pop();

                    }
                }
                else nNode = -1;
            }

        }

        #region Touch Stuff
        private object m_touchlock = new object();
        private Dictionary<int, TouchData> touches = new Dictionary<int, TouchData>();

        private event EventHandler<WMTouchEventArgs> Touchdown;
        private event EventHandler<WMTouchEventArgs> Touchup;
        private event EventHandler<WMTouchEventArgs> TouchMove;

        private void OnTouchDownHandler(object sender, WMTouchEventArgs e)
        {
            lock (m_touchlock)
            {
                TouchData t = new TouchData();
                t.Id = e.Id;
                t.IsNew = true;
                t.Pos = new Vector2(e.LocationX, e.LocationY);
                this.touches.Add(e.Id, t);
            }
        }

        private void OnTouchUpHandler(object sender, WMTouchEventArgs e)
        {
            lock (m_touchlock)
            {
                this.touches.Remove(e.Id);
            }
        }

        private void OnTouchMoveHandler(object sender, WMTouchEventArgs e)
        {
            lock (m_touchlock)
            {
                TouchData t = this.touches[e.Id];
                t.Pos = new Vector2(e.LocationX, e.LocationY);
            }
        }


        protected override void WndProc(ref Message m) // Decode and handle WM_TOUCH message.
        {
            bool handled;
            switch (m.Msg)
            {
                case TouchConstants.WM_TOUCH:
                    handled = DecodeTouch(ref m);
                    break;
                default:
                    handled = false;
                    break;
            }
            base.WndProc(ref m);  // Call parent WndProc for default message processing.

            if (handled) // Acknowledge event if handled.
                m.Result = new System.IntPtr(1);
        }

        private bool DecodeTouch(ref Message m)
        {
            // More than one touchinput may be associated with a touch message,
            int inputCount = (m.WParam.ToInt32() & 0xffff); // Number of touch inputs, actual per-contact messages
            TOUCHINPUT[] inputs = new TOUCHINPUT[inputCount];

            if (!TouchConstants.GetTouchInputInfo(m.LParam, inputCount, inputs, Marshal.SizeOf(new TOUCHINPUT())))
            {
                return false;
            }

            bool handled = false;
            for (int i = 0; i < inputCount; i++)
            {
                TOUCHINPUT ti = inputs[i];

                EventHandler<WMTouchEventArgs> handler = null;
                if ((ti.dwFlags & TouchConstants.TOUCHEVENTF_DOWN) != 0)
                {
                    handler = Touchdown;
                }
                else if ((ti.dwFlags & TouchConstants.TOUCHEVENTF_UP) != 0)
                {
                    handler = Touchup;
                }
                else if ((ti.dwFlags & TouchConstants.TOUCHEVENTF_MOVE) != 0)
                {
                    handler = TouchMove;
                }

                // Convert message parameters into touch event arguments and handle the event.
                if (handler != null)
                {
                    WMTouchEventArgs te = new WMTouchEventArgs();

                    // TOUCHINFO point coordinates and contact size is in 1/100 of a pixel; convert it to pixels.
                    // Also convert screen to client coordinates.
                    te.ContactY = ti.cyContact / 100;
                    te.ContactX = ti.cxContact / 100;
                    te.Id = ti.dwID;
                    {
                        Point pt = PointToClient(new Point(ti.x / 100, ti.y / 100));
                        te.LocationX = pt.X;
                        te.LocationY = pt.Y;
                    }
                    te.Time = ti.dwTime;
                    te.Mask = ti.dwMask;
                    te.Flags = ti.dwFlags;

                    handler(this, te);

                    // Mark this event as handled.
                    handled = true;
                }
            }
            TouchConstants.CloseTouchInputHandle(m.LParam);

            return handled;
        }
        #endregion

        #region Input Pins
        IPluginHost FHost;

        protected IHDEHost hde;



        [Import()]
        protected IPluginHost2 host2;

        [Import()]
        protected ILogger logger;

        [Input("Layers", Order = 1, IsSingle = true)]
        protected Pin<DX11Resource<DX11Layer>> FInLayer;

        [Input("Clear", DefaultValue = 1, Order = 2)]
        protected ISpread<bool> FInClear;

        [Input("Clear Depth", DefaultValue = 1, Order = 2)]
        protected ISpread<bool> FInClearDepth;

        [Input("Background Color", DefaultColor = new double[] { 0, 0, 0, 1 }, Order = 3)]
        protected ISpread<RGBAColor> FInBgColor;

        [Input("VSync", Visibility = PinVisibility.OnlyInspector, IsSingle = true)]
        protected ISpread<bool> FInVsync;

        [Input("Buffer Count", Visibility = PinVisibility.OnlyInspector, DefaultValue = 1, IsSingle = true)]
        protected ISpread<int> FInBufferCount;

        [Input("Do Not Wait", Visibility = PinVisibility.OnlyInspector, IsSingle = true)]
        protected ISpread<bool> FInDNW;

        [Input("Show Cursor", DefaultValue = 1, Visibility = PinVisibility.OnlyInspector)]
        protected IDiffSpread<bool> FInShowCursor;

        [Input("Fullscreen", Order = 5)]
        protected IDiffSpread<bool> FInFullScreen;

        [Input("Enable Depth Buffer", Order = 6, DefaultValue = 1)]
        protected IDiffSpread<bool> FInDepthBuffer;

        [Input("AA Samples per Pixel", DefaultEnumEntry = "1", EnumName = "DX11_AASamples")]
        protected IDiffSpread<EnumEntry> FInAASamplesPerPixel;

        /*[Input("AA Quality", Order = 8)]
        protected IDiffSpread<int> FInAAQuality;*/

        [Input("Enabled", DefaultValue = 1, Order = 9)]
        protected ISpread<bool> FInEnabled;

        [Input("View", Order = 10)]
        protected IDiffSpread<Matrix> FInView;

        [Input("Projection", Order = 11)]
        protected IDiffSpread<Matrix> FInProjection;

        [Input("Aspect Ratio", Order = 12, Visibility = PinVisibility.Hidden)]
        protected IDiffSpread<Matrix> FInAspect;

        [Input("Crop", Order = 13, Visibility = PinVisibility.OnlyInspector)]
        protected IDiffSpread<Matrix> FInCrop;

        [Input("ViewPort", Order = 20)]
        protected Pin<Viewport> FInViewPort;
        #endregion

        #region Output Pins
        [Output("Mouse State", AllowFeedback = true)]
        protected ISpread<MouseState> FOutMouseState;

        [Output("Keyboard State", AllowFeedback = true)]
        protected ISpread<KeyboardState> FOutKState;

        [Output("Touch Supported", IsSingle = true)]
        protected ISpread<bool> FOutTouchSupport;

        [Output("Touch Data", AllowFeedback = true)]
        protected ISpread<TouchData> FOutTouchData;

        [Output("Actual BackBuffer Size", AllowFeedback = true)]
        protected ISpread<Vector2D> FOutBackBufferSize;

        [Output("Texture Out")]
        protected ISpread<DX11Resource<DX11SwapChain>> FOutBackBuffer;

        protected ISpread<DX11Resource<DX11SwapChain>> FOuFS;

        [Output("Present Time", IsSingle = true)]
        protected ISpread<double> FOutPresent;

        [Output("Query", Order = 200, IsSingle = true)]
        protected ISpread<IDX11Queryable> FOutQueryable;

        [Output("Control", Order = 201, IsSingle = true, Visibility = PinVisibility.OnlyInspector)]
        protected ISpread<Control> FOutCtrl;

        [Output("Node Ref", Order = 201, IsSingle = true, Visibility = PinVisibility.OnlyInspector)]
        protected ISpread<INode> FOutRef;
        #endregion

        #region Fields
        public event DX11QueryableDelegate BeginQuery;
        public event DX11QueryableDelegate EndQuery;

        private Vector2D FMousePos;
        private Vector3D FMouseButtons;
        private List<Keys> FKeys = new List<Keys>();
        private int wheel = 0;

        private Dictionary<DX11RenderContext, DX11GraphicsRenderer> renderers = new Dictionary<DX11RenderContext, DX11GraphicsRenderer>();
        private List<DX11RenderContext> updateddevices = new List<DX11RenderContext>();
        private List<DX11RenderContext> rendereddevices = new List<DX11RenderContext>();
        private DepthBufferManager depthmanager;

        private DX11RenderSettings settings = new DX11RenderSettings();

        private bool FInvalidateSwapChain;
        private bool FResized = false;
        private DX11RenderContext primary;
        #endregion


        #region Evaluate (HACKED)
        public void Evaluate(int SpreadMax)
        {
            this.FOutCtrl[0] = this;
            this.FOutRef[0] = (INode)this.FHost;

            if (this.FOutQueryable[0] == null) { this.FOutQueryable[0] = this; }
            if (this.FOutBackBuffer[0] == null)
            {
                this.FOutBackBuffer[0] = new DX11Resource<DX11SwapChain>();
                this.FOuFS = new Spread<DX11Resource<DX11SwapChain>>();
                this.FOuFS.SliceCount = 1;
                this.FOuFS[0] = new DX11Resource<DX11SwapChain>();
            }

            this.updateddevices.Clear();
            this.rendereddevices.Clear();
            this.FInvalidateSwapChain = false;

            if (!this.depthmanager.FormatChanged) // do not clear reset if format changed
            {
                this.depthmanager.NeedReset = false;
            }
            else
            {
                this.depthmanager.FormatChanged = false; //Clear flag ok
            }

            if (FInAASamplesPerPixel.IsChanged || this.FInBufferCount.IsChanged)
            {
                this.depthmanager.NeedReset = true;
                this.FInvalidateSwapChain = true;
            }

            if (this.FInFullScreen.IsChanged)
            {
                string path;
                this.FHost.GetNodePath(false, out path);
                INode2 n2 = hde.GetNodeFromPath(path);

                if (n2.Window != null)
                {
                    if (n2.Window.IsVisible)
                    {
                        if (this.FInFullScreen[0])
                        {
                            hde.SetComponentMode(n2, ComponentMode.Fullscreen);
                        }
                        else
                        {
                            hde.SetComponentMode(n2, ComponentMode.InAWindow);
                        }
                    }
                }
            }

            /*if (this.FInFullScreen.IsChanged)
            {
                if (this.FInFullScreen[0])
                {
                    string path;
                    this.FHost.GetNodePath(false, out path);
                    INode2 n2 = hde.GetNodeFromPath(path);
                    hde.SetComponentMode(n2, ComponentMode.Fullscreen);
                }
                else
                {
                    string path;
                    this.FHost.GetNodePath(false, out path);
                    INode2 n2 = hde.GetNodeFromPath(path);
                    hde.SetComponentMode(n2, ComponentMode.InAWindow);
                }
            }*/

            this.FOutKState[0] = new KeyboardState(this.FKeys);
            this.FOutMouseState[0] = MouseState.Create(this.FMousePos.x, this.FMousePos.y, this.FMouseButtons.x > 0.5f, this.FMouseButtons.y > 0.5f, this.FMouseButtons.z > 0.5f, false, false, this.wheel);
            this.FOutBackBufferSize[0] = new Vector2D(this.Width, this.Height);

            this.FOutTouchSupport[0] = this.touchsupport;

            this.FOutTouchData.SliceCount = this.touches.Count;

            int tcnt = 0;
            float fw = (float)this.ClientSize.Width;
            float fh = (float)this.ClientSize.Height;
            lock (m_touchlock)
            {
                foreach (int key in touches.Keys)
                {
                    TouchData t = touches[key];

                    this.FOutTouchData[tcnt] = t.Clone(fw, fh);
                    t.IsNew = false;
                    tcnt++;
                }
            }


            AfterEvaluate();
        }
        #endregion

        #region Dispose
        void IDisposable.Dispose()
        {
            if (this.FOutBackBuffer[0] != null) { this.FOutBackBuffer[0].Dispose(); }
        }
        #endregion

        #region Is Enabled
        public bool IsEnabled
        {
            get { return this.FInEnabled[0]; }
        }
        #endregion

        #region Render
        public void Render(DX11RenderContext context)
        {
            Device device = context.Device;

            if (!this.updateddevices.Contains(context)) { this.Update(null, context); }

            if (this.rendereddevices.Contains(context)) { return; }

            Exception exception = null;

            if (this.FInEnabled[0])
            {

                if (this.BeginQuery != null)
                {
                    this.BeginQuery(context);
                }

                DX11SwapChain chain = this.FOutBackBuffer[0][context];
                DX11GraphicsRenderer renderer = this.renderers[context];

                renderer.EnableDepth = this.FInDepthBuffer[0];
                renderer.DepthStencil = this.depthmanager.GetDepthStencil(context);
                renderer.DepthMode = this.depthmanager.Mode;
                renderer.SetRenderTargets(chain);
                renderer.SetTargets();

                try
                {
                    if (this.FInClear[0])
                    {
                        //Remove Shader view if bound as is
                        context.CurrentDeviceContext.ClearRenderTargetView(chain.RTV, this.FInBgColor[0].Color);
                    }

                    if (this.FInClearDepth[0])
                    {
                        if (this.FInDepthBuffer[0])
                        {
                            this.depthmanager.Clear(context);
                        }
                    }

                    //Only call render if layer connected
                    if (this.FInLayer.PluginIO.IsConnected)
                    {
                        int rtmax = Math.Max(this.FInProjection.SliceCount, this.FInView.SliceCount);
                        rtmax = Math.Max(rtmax, this.FInViewPort.SliceCount);

                        settings.ViewportCount = rtmax;

                        bool viewportpop = this.FInViewPort.PluginIO.IsConnected;

                        for (int i = 0; i < rtmax; i++)
                        {
                            this.RenderSlice(context, settings, i, viewportpop);
                        }
                    }

                    if (this.EndQuery != null)
                    {
                        this.EndQuery(context);
                    }
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
                finally
                {
                    renderer.CleanTargets();
                }
            }

            this.rendereddevices.Add(context);

            //Rethrow
            if (exception != null)
            {
                throw exception;
            }


            AfterRender(context);
        }
        #endregion

        #region RenderSlice (HACKED)
        private void RenderSlice(DX11RenderContext context, DX11RenderSettings settings, int i, bool viewportpop)
        {
            float cw = (float)this.ClientSize.Width;
            float ch = (float)this.ClientSize.Height;

            settings.ViewportIndex = i;
            settings.View = this.FInView[i];

            Matrix proj = this.FInProjection[i];
            Matrix aspect = Matrix.Invert(this.FInAspect[i]);
            Matrix crop = Matrix.Invert(this.FInCrop[i]);


            settings.Projection = proj * aspect * crop;
            settings.ViewProjection = settings.View * settings.Projection;
            settings.BackBuffer = this.FOutBackBuffer[0][context];
            settings.RenderWidth = this.FOutBackBuffer[0][context].Resource.Description.Width;
            settings.RenderHeight = this.FOutBackBuffer[0][context].Resource.Description.Height;
            settings.ResourceSemantics.Clear();
            settings.CustomSemantics.Clear();

            if (viewportpop)
            {
                context.RenderTargetStack.PushViewport(this.FInViewPort[i].Normalize(cw, ch));
            }


            //Call render on all layers
            for (int j = 0; j < this.FInLayer.SliceCount; j++)
            {

                this.FInLayer[j][context].Render(this.FInLayer.PluginIO, context, settings);

                DrawSelectedNode(context);
            }

            if (viewportpop)
            {
                context.RenderTargetStack.PopViewport();
            }
        }
        #endregion

        #region Update
        public void Update(IPluginIO pin, DX11RenderContext context)
        {
            Device device = context.Device;

            if (this.updateddevices.Contains(context)) { return; }

            int samplecount = Convert.ToInt32(FInAASamplesPerPixel[0].Name);

            SampleDescription sd = new SampleDescription(samplecount, 0);

            if (this.FResized || this.FInvalidateSwapChain || this.FOutBackBuffer[0][context] == null)
            {
                this.FOutBackBuffer[0].Dispose(context);

                List<SampleDescription> sds = context.GetMultisampleFormatInfo(Format.R8G8B8A8_UNorm);
                int maxlevels = sds[sds.Count - 1].Count;

                if (sd.Count > maxlevels)
                {
                    logger.Log(LogType.Warning, "Multisample count too high for this format, reverted to: " + maxlevels);
                    sd.Count = maxlevels;
                }

                this.FOutBackBuffer[0][context] = new DX11SwapChain(context, this.Handle, Format.R8G8B8A8_UNorm, sd, 60);//, this.FInBufferCount[0]);

#if DEBUG
                this.FOutBackBuffer[0][context].Resource.DebugName = "BackBuffer";
#endif
                this.depthmanager.NeedReset = true;
            }

            DX11SwapChain sc = this.FOutBackBuffer[0][context];

            if (this.FResized)
            {

                //if (!sc.IsFullScreen)
                //{
                // sc.Resize();
                // }
                //this.FInvalidateSwapChain = true;
            }


            if (!this.renderers.ContainsKey(context)) { this.renderers.Add(context, new DX11GraphicsRenderer(this.FHost, context)); }

            this.depthmanager.Update(context, sc.Width, sc.Height, sd);

            this.updateddevices.Add(context);
        }
        #endregion

        #region Destroy
        public void Destroy(IPluginIO pin, DX11RenderContext context, bool force)
        {
            //if (this.FDepthManager != null) { this.FDepthManager.Dispose(); }

            if (this.renderers.ContainsKey(context)) { this.renderers.Remove(context); }

            this.FOutBackBuffer[0].Dispose(context);
        }
        #endregion

        #region Render Window
        public void Present()
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                PresentFlags flags = this.FInDNW[0] ? (PresentFlags)8 : PresentFlags.None;
                if (this.FInVsync[0])
                {
                    this.FOutBackBuffer[0][this.RenderContext].Present(1, flags);
                }
                else
                {
                    this.FOutBackBuffer[0][this.RenderContext].Present(0, flags);
                }
            }
            catch
            {

            }

            sw.Stop();
            this.FOutPresent[0] = sw.Elapsed.TotalMilliseconds;

            this.FResized = false;
        }

        public DX11RenderContext RenderContext
        {
            get { return this.primary; }
            set
            {
                this.primary = value;
            }
        }

        public IntPtr WindowHandle
        {
            get
            {
                return this.Handle;
            }
        }
        #endregion

        #region IsVisible
        public bool IsVisible
        {
            get
            {
                INode node = (INode)this.FHost;

                if (node.Window != null)
                {
                    return node.Window.IsVisible();
                }
                else
                {
                    return false;
                }
            }
        }
        #endregion

        #region Constructor
        private bool cvisible = true;
        [ImportingConstructor()]
        public DX11_EditorRendererNode(IPluginHost host, IIOFactory iofactory, IHDEHost hdehost)
        {
            InitializeComponent();
            this.FHost = host;
            this.hde = hdehost;
            this.BackColor = System.Drawing.Color.Black;
            //this.hde.BeforeComponentModeChange += new ComponentModeEventHandler(hde_BeforeComponentModeChange);
            this.Resize += DX11RendererNode_Resize;
            this.Load += new EventHandler(DX11RendererNode_Load);
            this.Click += new EventHandler(DX11RendererNode_Click);
            this.MouseEnter += new EventHandler(DX11RendererNode_MouseEnter);
            this.MouseLeave += new EventHandler(DX11RendererNode_MouseLeave);
            this.LostFocus += new EventHandler(DX11RendererNode_LostFocus);
            this.MouseWheel += new System.Windows.Forms.MouseEventHandler(DX11RendererNode_MouseWheel);
            Touchdown += OnTouchDownHandler;
            Touchup += OnTouchUpHandler;
            TouchMove += OnTouchMoveHandler;
            this.depthmanager = new DepthBufferManager(host, iofactory);
        }

        #endregion

        #region Window Stuff (HACKED)
        public IntPtr InputWindowHandle
        {
            get { return this.Handle; }
        }

        public RGBAColor BackgroundColor
        {
            get { return new RGBAColor(0, 0, 0, 1); }
        }


        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // DX11RendererNode
            // 
            this.BackColor = System.Drawing.Color.Black;
            this.Name = "DX11RendererNode";
            this.ResumeLayout(false);


            AfterInitializeComponent();

        }









        private bool touchsupport;
        void DX11RendererNode_Load(object sender, EventArgs e)
        {
            if (!TouchConstants.RegisterTouchWindow(this.Handle, 0))
                this.touchsupport = false;
            else
                this.touchsupport = true;
        }
        void hde_BeforeComponentModeChange(object sender, ComponentModeEventArgs args)
        {
            /*if (args.ComponentMode == ComponentMode.Fullscreen)
            {
            this.FOutBackBuffer[0][this.RenderContext].SetFullScreen(true);
            }*/
        }
        void DX11RendererNode_MouseWheel(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            this.wheel += e.Delta / 112;
        }
        void DX11RendererNode_Click(object sender, EventArgs e)
        {
            /*INode2 node = (INode2)this.host2;
            hde.SelectNodes(new INode2[1] { node });
            Console.Write("Test");*/
        }
        void DX11RendererNode_LostFocus(object sender, EventArgs e)
        {
            //Cursor.Show();
        }
        void DX11RendererNode_MouseLeave(object sender, EventArgs e)
        {
            if (!this.cvisible)
            {
                this.cvisible = true;
                Cursor.Show();
            }
        }
        void DX11RendererNode_MouseEnter(object sender, EventArgs e)
        {
            if (this.FInShowCursor.SliceCount > 0)
            {
                if (!this.FInShowCursor[0] && this.cvisible)
                {
                    Cursor.Hide();
                    this.cvisible = false;
                }
            }
        }
        private void DX11RendererNode_Resize(object sender, EventArgs e)
        {
            this.FResized = true;
        }
        private void DX11RendererNode_VisibleChanged(object sender, EventArgs e)
        {
            if (Visible)
            {
                this.FResized = true;
            }
        }
        protected override void OnMouseMove(System.Windows.Forms.MouseEventArgs e)
        {
            double mx = e.X;
            double my = e.Y;
            mx = VMath.Map(mx, 0, this.Width, -1.0, 1.0, TMapMode.Clamp);
            my = VMath.Map(my, 0, this.Height, 1.0, -1.0, TMapMode.Clamp);
            this.FMousePos.x = mx;
            this.FMousePos.y = my;
            base.OnMouseMove(e);
        }
        protected override void OnMouseDown(System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left) { this.FMouseButtons.x = 1; }
            if (e.Button == System.Windows.Forms.MouseButtons.Middle) { this.FMouseButtons.y = 1; }
            if (e.Button == System.Windows.Forms.MouseButtons.Right) { this.FMouseButtons.z = 1; }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left) { this.FMouseButtons.x = 0; }
            if (e.Button == System.Windows.Forms.MouseButtons.Middle) { this.FMouseButtons.y = 0; }
            if (e.Button == System.Windows.Forms.MouseButtons.Right) { this.FMouseButtons.z = 0; }
            base.OnMouseUp(e);
        }
        protected override bool ProcessKeyEventArgs(ref Message m)
        {
            return base.ProcessKeyEventArgs(ref m);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            //e.KeyData == Keys.
            base.OnKeyDown(e);
            if (!this.FKeys.Contains(e.KeyCode))
            {
                this.FKeys.Add(e.KeyCode);
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (this.FKeys.Contains(e.KeyCode))
            {
                this.FKeys.Remove(e.KeyCode);
            }
            base.OnKeyUp(e);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            e.Handled = true;
            //Se.SuppressKeyPress = true;
            base.OnKeyPress(e);
        }
        #endregion
    }
}
