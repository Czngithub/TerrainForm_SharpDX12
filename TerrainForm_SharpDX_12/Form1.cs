using System;
using SharpDX.DXGI;
using System.Drawing;
using System.Windows.Forms;

namespace TerrainForm_SharpDX_12
{
    using SharpDX;
    using SharpDX.Direct3D12;
    using TextureLoader;

    public partial class Form1 : Form
    {
        private Device device;//定义设备
        private SwapChain3 swapChain;//定义交换链

        private CommandAllocator commandAllocator;//定义命令分配器
        private CommandQueue commandQueue;//定义命令队列
        private GraphicsCommandList commandList;//定义命令列表
        private PipelineState pipelineState;//定义管道状态
        private RootSignature rootSignature;//定义根签名

        private readonly Resource[] renderTargets;//定义渲染目标视图
        private DescriptorHeap renderTargetViewHeap;//定义描述符堆
        private int rtvDescriptorSize;//描述符堆句柄增量

        private ViewportF viewPort;//定义视口
        private Rectangle scissorRectangle;//定义裁剪矩形
        private Matrix view;//定义摄像机
        private Matrix proj;//定义投影矩阵

        public Vector3 CamPostion = new Vector3(0, 100, 100);//定义摄像机位置
        public Vector3 CamTarget = new Vector3(125, 30, 125);//定义目标位置

        private float angleY = 0.01f;//定义绕Y轴旋转变量

        private int mouseLastX, mouseLastY;//定义记录鼠标按下时的坐标位置
        private bool isRotateByMouse = false;//定义记录是否由鼠标控制旋转
        private bool isMoveByMouse = false;//定义记录是否由鼠标控制移动

        private PositionTexture[] vertices;//定义顶点变量
        private Resource texture;//定义贴图
        private Resource material;//定义材质
        private Resource vertexBuffer;//定义顶点缓冲区
        private Resource indexBuffer;//定义索引缓冲区
        VertexBufferView vertexBufferView;//定义顶点缓冲区
        IndexBufferView indexBufferView;//定义索引缓冲区
        private int[] indices;//定义索引号

        private int width = 800, height = 600;//定义窗口尺寸
        private int xCount = 5, yCount = 4;//定义横向和纵向网格数目
        private float cellHeight = 1f, cellWidth = 1f;//定义单元的宽度和长度
        private int frameIndex;

        struct PositionTexture
        {
            public Vector3 Position;
            public float Tu;
            public float Tv;
        }

        const int count = 2;

        //创建窗口
        public Form1()
        {
            this.ClientSize = new Size(width, height);
            this.Text = "地形";
        }
        //初始化
        public bool Initialize()
        {
            viewPort = new ViewportF(0, 0, width, height);//创建视口
            scissorRectangle = new Rectangle(0, 0, width, height);//裁剪矩形
#if DEBUG
            //启用调试层
            {
                DebugInterface.Get().EnableDebugLayer();
            }
#endif
            //创建设备
            device = new Device(null, SharpDX.Direct3D.FeatureLevel.Level_11_0);
            using (var factory = new Factory4())
            {
                //描述并创建命令队列
                CommandQueueDescription queueDesc = new CommandQueueDescription(CommandListType.Direct);
                commandQueue = device.CreateCommandQueue(queueDesc);
                //描述交换链
                SwapChainDescription swapChainDesc = new SwapChainDescription()
                {
                    BufferCount = count,
                    ModeDescription = new ModeDescription(
                        width, height,//缓存大小，一般与窗口大小相同
                        new Rational(60, 1),//刷新率，60hz
                        Format.R8G8B8A8_UNorm),//像素格式，8位RGBA格式
                    Usage = Usage.RenderTargetOutput,//CPU访问缓冲权限
                    SwapEffect = SwapEffect.FlipDiscard,//描述处理曲面后的缓冲区内容
                    OutputHandle = Handle,//获取渲染窗口句柄
                    Flags = SwapChainFlags.None,//描述交换链的行为
                    SampleDescription = new SampleDescription(1, 0),//一重采样
                    IsWindowed = true//窗口显示
                };
                //创建交换链
                SwapChain tempSwapChain = new SwapChain(factory, commandQueue, swapChainDesc);
                swapChain = tempSwapChain.QueryInterface<SwapChain3>();
                tempSwapChain.Dispose();
                frameIndex = swapChain.CurrentBackBufferIndex;//获取交换链的当前缓冲区的索引
            }
            //创建描述符堆
            var rtvHeapDesc = new DescriptorHeapDescription()
            {
                DescriptorCount = count,//堆中的描述符数
                Flags = DescriptorHeapFlags.None,//None表示堆的默认用法
                Type = DescriptorHeapType.RenderTargetView//堆中的描述符类型
            };
            //获取给定类型的描述符堆的句柄增量的大小，将句柄按正确的数量递增到描述符数组中
            rtvDescriptorSize = device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);//获取给定类型的描述符堆的句柄增量的大小，将句柄按正确的数量递增到描述符数组中
            //创建渲染目标视图
            CpuDescriptorHandle rtvHandle = renderTargetViewHeap.CPUDescriptorHandleForHeapStart;//获取堆中起始的CPU描述符句柄，for循环为交换链中的每一个缓冲区都创建了一个RTV
            for (int n = 0; n < count; n++)
            {
                //获得交换链的第n个缓冲区
                renderTargets[n] = swapChain.GetBackBuffer<Resource>(n);
                device.CreateRenderTargetView(
                    renderTargets[n],//指向渲染目标对象的指针
                    null,//指向描述渲染目标视图结构的指针
                    rtvHandle);//CPU描述符句柄，表示渲染目标视图的堆的开始
                rtvHandle += rtvDescriptorSize;
            }
            //创建命令分配器对象
            commandAllocator = device.CreateCommandAllocator(CommandListType.Direct);
            //创建根签名
            var rootSignatureDesc = new RootSignatureDescription(
                RootSignatureFlags.AllowInputAssemblerInputLayout,
                //创建根常量
                new[]
                {
                    new RootParameter(
                        ShaderVisibility.All,//指定可以访问根签名绑定的内容的着色器
                        new DescriptorRange()
                        {
                            RangeType = DescriptorRangeType.ShaderResourceView,//指定描述符范围
                            BaseShaderRegister = 0,//指定描述符范围内的基本着色器
                            OffsetInDescriptorsFromTableStart = int.MinValue,//描述符从根签名开始的偏移量
                            DescriptorCount = 1//描述符范围内的描述符数
                        })
                });
            //表示该根签名需要一组顶点缓冲区来绑定
            rootSignature = device.CreateRootSignature(rootSignatureDesc.Serialize());
            //描述输入装配器阶段的输入元素，这里定义顶点输入布局
            var inputElementDescs = new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32A32_Float, 0, 0),
                new InputElement("COLOR", 0, Format.R32G32B32A32_Float, 16, 0)
            };
            //创建着色器
            var vertexShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "VS", "vs_5_0", SharpDX.D3DCompiler.ShaderFlags.Debug));
            var pixelShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "PS", "ps_5_0", SharpDX.D3DCompiler.ShaderFlags.Debug));
            //描述和创建流水线状态对象（PSO）
            var psoDesc = new GraphicsPipelineStateDescription()
            {
                InputLayout = new InputLayoutDescription(inputElementDescs),    //描述输入缓冲器
                RootSignature = rootSignature,//根签名
                VertexShader = vertexShader,//顶点着色器
                PixelShader = pixelShader,//像素着色器
                RasterizerState = RasterizerStateDescription.Default(),//描述光栅器状态
                BlendState = BlendStateDescription.Default(),//描述混合状态
                DepthStencilFormat = SharpDX.DXGI.Format.D32_Float,//描述深度/模板格式（纹理资源）
                DepthStencilState = new DepthStencilStateDescription()//描述深度模板状态
                {
                    IsDepthEnabled = false,//不启用深度测试
                    IsStencilEnabled = false//不启用模板测试
                },
                SampleMask = int.MaxValue,//混合状态的样本掩码
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,//定义该管道的几何或外壳着色器的输入类型，这里是三角
                RenderTargetCount = 1,//RTVFormat成员中的渲染目标格式数
                Flags = PipelineStateFlags.None,//用于控制管道状态的标志，这里表示没有标志
                SampleDescription = new SampleDescription(1, 0),//描述资源的多采样参数
                StreamOutput = new StreamOutputDescription()//描述输出缓冲器
            };
            psoDesc.RenderTargetFormats[0] = Format.R8G8B8A8_UNorm;//描述渲染目标格式的数组
            //设置渲染流水线
            pipelineState = device.CreateGraphicsPipelineState(psoDesc);
            //创建命令列表
            commandList = device.CreateCommandList(
                CommandListType.Direct,//指定命令列表的创建类型，Direct命令列表不会继承任何GPU状态
                commandAllocator,//指向设备创建的命令列表对象的指针
                pipelineState);//指向(管道)内存块的指针

            commandList.Close();
            return true;
        }

        //定义顶点
        public void VertexDeclaration()
        {
            string bitmapPath = @"C:\Users\yulanli\source\repos\TerrainForm_SharpDX_12\heightMap.BMP";
            Bitmap bitmap = new Bitmap(bitmapPath);
            xCount = (bitmap.Width - 1) / 2;
            yCount = (bitmap.Height - 1) / 2;
            cellWidth = bitmap.Width / xCount;
            cellHeight = bitmap.Height / yCount;
            vertices = new PositionTexture[(xCount + 1) * (yCount + 1)];//定义顶点
            for (int i = 0; i < yCount + 1; i++)
            {
                for (int j = 0; j < xCount + 1; j++)
                {
                    System.Drawing.Color color = bitmap.GetPixel((int)(j * cellWidth), (int)(i * cellHeight));
                    float height = float.Parse(color.R.ToString()) + float.Parse(color.G.ToString()) + float.Parse(color.B.ToString());
                    height /= 10;
                    vertices[j + i * (xCount + 1)].Position = new Vector3(j * cellWidth, height, i * cellHeight);
                    vertices[j + i * (xCount + 1)].Tu = (float)j / (xCount + 1);
                    vertices[j + i * (xCount + 1)].Tv = (float)i / (yCount + 1);
                }
            }
            var vertexBufferSize = Utilities.SizeOf(vertices);
            //使用上传堆来传递顶点缓冲区的数据
            vertexBuffer = device.CreateCommittedResource(
                new HeapProperties(HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(vertexBufferSize),
                ResourceStates.GenericRead
                );
            //将顶点的数据复制到顶点缓冲区
            IntPtr pVertexDataBegin = vertexBuffer.Map(0);
            Utilities.Write(
                pVertexDataBegin,
                vertices,
                0,
                vertices.Length);
            vertexBuffer.Unmap(0);
            //初始化顶点缓冲区视图
            vertexBufferView = new VertexBufferView();
            vertexBufferView.BufferLocation = vertexBuffer.GPUVirtualAddress;
            vertexBufferView.StrideInBytes = Utilities.SizeOf<PositionTexture>();
            vertexBufferView.SizeInBytes = vertexBufferSize;
            //设置摄像机目标位置
            CamTarget = new Vector3(bitmap.Width / 2, 0f, bitmap.Height / 2);
        }

        //定义索引
        private void IndicesDeclaration()
        {
            indices = new int[6 * xCount * yCount];
            for (int i = 0; i < yCount; i++)
            {
                for (int j = 0; j < xCount; j++)
                {
                    indices[6 * (j + i * xCount)] = j + i * (xCount + 1);
                    indices[6 * (j + i * xCount) + 1] = j + (i + 1) * (xCount + 1);
                    indices[6 * (j + i * xCount) + 2] = j + i * (xCount + 1) + 1;
                    indices[6 * (j + i * xCount) + 3] = j + i * (xCount + 1) + 1;
                    indices[6 * (j + i * xCount) + 4] = j + (i + 1) * (xCount + 1);
                    indices[6 * (j + i * xCount) + 5] = j + (i + 1) * (xCount + 1) + 1;
                }
            }
            //使用上传堆来传递索引缓冲区的数据
            int indexBufferSize = Utilities.SizeOf(indices);
            indexBuffer = device.CreateCommittedResource(
                new HeapProperties(HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(indexBufferSize),
                ResourceStates.GenericRead);
            //将索引的数据复制到索引缓冲区
            IntPtr pIndexDataBegin = indexBuffer.Map(0);
            Utilities.Write(
                pIndexDataBegin,
                indices,
                0,
                indices.Length);
            indexBuffer.Unmap(0);
            //初始化索引缓冲区视图
            indexBufferView = new IndexBufferView();
            indexBufferView.BufferLocation = indexBuffer.GPUVirtualAddress;
            indexBufferView.SizeInBytes = indexBufferSize;
            indexBufferView.Format = Format.R32_UInt;
        }

        //导入贴图和材质
        private void LoadTexturesAndMaterials()
        {
            TextureUtilities.CreateTextureFromBitmap(device, @"C:\Users\yulanli\Desktop\TerrainForm\colorMap.jpg");
        }

        public void Render()
        {
            
        }
    }
}
