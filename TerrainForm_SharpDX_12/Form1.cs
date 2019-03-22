﻿using System;
using SharpDX.DXGI;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace TerrainForm_SharpDX_12
{
    //限定该作用域内的属性如Device等属于SharpDX12
    using SharpDX.Direct3D12;
    using SharpDX;


    //创建窗口
    public partial class Form1 : Form
    {
        private Device device;//定义设备
        private SwapChain3 swapChain;//定义交换链

        private CommandAllocator commandAllocator;//定义命令分配器
        private CommandAllocator bundleAllocator;
        private CommandQueue commandQueue;//定义命令队列
        private GraphicsCommandList commandList;//定义命令列表
        private GraphicsCommandList bundle;
        private PipelineState pipelineState;//定义管道状态
        private RootSignature rootSignature;//定义根签名

        private readonly Resource[] renderTargets = new Resource[FrameCount];//定义渲染目标视图
        private Resource constantBuffer;
        private DescriptorHeap renderTargetViewHeap;//定义描述符堆
        private DescriptorHeap constantBufferViewHeap;//定义着色器资源视图堆
        private DescriptorHeap shaderRenderViewHeap;
        private int rtvDescriptorSize;//描述符堆句柄增量
        private int srvDescriptorSize;
        IntPtr constantBufferPointer;

        private Fence fence;//围栏
        private int fenceValue;//围栏描述符数值
        private AutoResetEvent fenceEvent;  //帧同步事件

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

        private Resource texture;//定义贴图
        private Resource material;//定义材质
        private Resource vertexBuffer;//定义顶点缓冲区
        private Resource indexBuffer;//定义索引缓冲区
        VertexBufferView vertexBufferView;//定义顶点缓冲区
        IndexBufferView indexBufferView;//定义索引缓冲区
        private int[] indices;//定义索引号

        private int width, height;//定义窗口尺寸
        private int xCount = 5, yCount = 4;//定义横向和纵向网格数目
        private float cellHeight = 1f, cellWidth = 1f;//定义单元的宽度和长度
        private int frameIndex;

        const int FrameCount = 2;

        public struct ConstantBuffer
        {
            public Vector4 Offset;
        };

        public struct PositionTextured
        {
            public Vector3 Position;
            public Vector2 Texcoord;
        }

        //初始化
        public bool Initialize(SharpDX.Windows.RenderForm form)
        {
            LoadPipeline(form);
            LoadAssets();
            return true;
        }

        //创建设备
        private void LoadPipeline(SharpDX.Windows.RenderForm form)
        {
            width = form.ClientSize.Width;
            height = form.ClientSize.Height;

            //创建视口
            viewPort = new ViewportF(0, 0, width, height);

            //创建裁剪矩形
            scissorRectangle = new Rectangle(0, 0, width, height);

#if DEBUG
            //启用调试层
            {
                DebugInterface.Get().EnableDebugLayer();
            }
#endif
            device = new Device(null, SharpDX.Direct3D.FeatureLevel.Level_11_0);
            //工厂化
            using (var factory = new Factory4())
            {
                //创建命令队列
                CommandQueueDescription queueDesc = new CommandQueueDescription(CommandListType.Direct);
                commandQueue = device.CreateCommandQueue(queueDesc);

                //创建交换链
                SwapChainDescription swapChainDesc = new SwapChainDescription()
                {
                    BufferCount = FrameCount,
                    ModeDescription = new ModeDescription(
                        width, height,//缓存大小，一般与窗口大小相同
                        new Rational(60, 1),//刷新率，60hz
                        Format.R8G8B8A8_UNorm),//像素格式，8位RGBA格式
                    Usage = Usage.RenderTargetOutput,//CPU访问缓冲权限
                    SwapEffect = SwapEffect.FlipDiscard,//描述处理曲面后的缓冲区内容
                    OutputHandle = form.Handle,//获取渲染窗口句柄
                    Flags = SwapChainFlags.None,//描述交换链的行为
                    SampleDescription = new SampleDescription(1, 0),//一重采样
                    IsWindowed = true//true为窗口显示，false为全屏显示
                };

                //创建交换链
                SwapChain tempSwapChain = new SwapChain(factory, commandQueue, swapChainDesc);
                swapChain = tempSwapChain.QueryInterface<SwapChain3>();
                tempSwapChain.Dispose();
                frameIndex = swapChain.CurrentBackBufferIndex;//获取交换链的当前缓冲区的索引
            }

            //创建描述符堆
            //创建一个渲染目标视图（RTV）的描述符堆
            DescriptorHeapDescription rtvHeapDesc = new DescriptorHeapDescription()
            {
                DescriptorCount = FrameCount,//堆中的描述符数
                Flags = DescriptorHeapFlags.None,//结果值指定符堆，None表示堆的默认用法
                Type = DescriptorHeapType.RenderTargetView//堆中的描述符类型
            };
            renderTargetViewHeap = device.CreateDescriptorHeap(rtvHeapDesc);

            //获取给定类型的描述符堆的句柄增量的大小，将句柄按正确的数量递增到描述符数组中
            rtvDescriptorSize = device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);

            //创建一个CBV的描述符堆
            var cbvHeapDesc = new DescriptorHeapDescription()
            {
                DescriptorCount = 1,
                Flags = DescriptorHeapFlags.ShaderVisible,
                Type = DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView
            };
            constantBufferViewHeap = device.CreateDescriptorHeap(cbvHeapDesc);
            //创建一个SRV的描述符堆
            var srvHeapDesc = new DescriptorHeapDescription()
            {
                DescriptorCount = 1,
                Flags = DescriptorHeapFlags.ShaderVisible,
                Type = DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView
            };
            shaderRenderViewHeap = device.CreateDescriptorHeap(srvHeapDesc);
            srvDescriptorSize = device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
            //构建资源描述符来填充描述符堆
            //获取指向描述符堆起始处的指针
            CpuDescriptorHandle srvHandle = shaderRenderViewHeap.CPUDescriptorHandleForHeapStart;
            
            //创建渲染目标视图
            //获取堆中起始的CPU描述符句柄，for循环为交换链中的每一个缓冲区都创建了一个RTV(渲染目标视图)
            CpuDescriptorHandle rtvHandle = renderTargetViewHeap.CPUDescriptorHandleForHeapStart;
            for (int n = 0; n < FrameCount; n++)
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
            bundleAllocator = device.CreateCommandAllocator(CommandListType.Bundle);
        }

        //创建资源
        private void LoadAssets()
        {
            //创建一个空的根签名
            var rootSignatureDesc = new RootSignatureDescription(
                RootSignatureFlags.AllowInputAssemblerInputLayout,
                //根常量
                new[] {
                    new RootParameter(ShaderVisibility.All,//指定可以访问根签名绑定的内容的着色器，这里设置为顶点着色器
                    new DescriptorRange()
                    {
                        RangeType = DescriptorRangeType.ConstantBufferView,//指定描述符范围，这里的参数是CBV
                        BaseShaderRegister = 0,//指定描述符范围内的基本着色器
                        OffsetInDescriptorsFromTableStart = int.MinValue,//描述符从根签名开始的偏移量
                        DescriptorCount = 1//描述符范围内的描述符数
                    })
                });
                
            //表示该根签名需要一组顶点缓冲区来绑定
            rootSignature = device.CreateRootSignature(rootSignatureDesc.Serialize());

            //创建流水线状态，负责编译和加载着色器
#if DEBUG
            var vertexShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "VS", "vs_5_0", SharpDX.D3DCompiler.ShaderFlags.Debug));
#else
            var vertexShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "VS", "vs_5_0"));
#endif

//#if DEBUG
//            var pixelShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "PS", "ps_5_0", SharpDX.D3DCompiler.ShaderFlags.Debug));
//#else
//            var pixelShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "PS", "ps_5_0"));
//#endif

            //描述输入装配器阶段的输入元素，这里定义顶点输入布局
            var inputElementDescs = new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElement("TEXCOORD", 0, Format.R32G32_Float, 12, 0)
            };

            //创建流水线状态对象（PSO）
            var psoDesc = new GraphicsPipelineStateDescription()
            {
                InputLayout = new InputLayoutDescription(inputElementDescs),//描述输入缓冲器
                RootSignature = rootSignature,//根签名
                VertexShader = vertexShader,//顶点着色器
                //PixelShader = pixelShader,//像素着色器
                RasterizerState = RasterizerStateDescription.Default(),//描述光栅器状态
                BlendState = BlendStateDescription.Default(),//描述混合状态
                DepthStencilFormat = SharpDX.DXGI.Format.D32_Float,//描述深度/模板格式（纹理资源）
                DepthStencilState = DepthStencilStateDescription.Default(),//描述深度模板状态
                SampleMask = int.MaxValue,//混合状态的样本掩码
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,//定义该管道的几何或外壳着色器的输入类型，这里是三角
                RenderTargetCount = 1,//RTVFormat成员中的渲染目标格式数
                Flags = PipelineStateFlags.None,//用于控制管道状态的标志，这里表示没有标志
                SampleDescription = new SampleDescription(1, 0),//描述资源的多采样参数
                StreamOutput = new StreamOutputDescription()//描述输出缓冲器
            };
            psoDesc.RenderTargetFormats[0] = Format.R8G8B8A8_UNorm;//描述渲染目标格式的数组

            //设置管道
            pipelineState = device.CreateGraphicsPipelineState(psoDesc);

            //创建命令列表
            commandList = device.CreateCommandList(
                CommandListType.Direct,//指定命令列表的创建类型，Direct命令列表不会继承任何GPU状态
                commandAllocator,//指向设备创建的命令列表对象的指针
                pipelineState);//指向(管道)内存块的指针

            commandList.Close();

            float aspectRatio = viewPort.Width / viewPort.Height;

            //定义待绘制图形的几何形状
            string bitmapPath = @"C:\Users\yulanli\Desktop\TerrainForm\heightMap.BMP";
            Bitmap bitmap = new Bitmap(bitmapPath);
            xCount = (bitmap.Width - 1) / 2;
            yCount = (bitmap.Height - 1) / 2;
            cellWidth = bitmap.Width / xCount;
            cellHeight = bitmap.Height / yCount;

            var vertices = new PositionTextured[(xCount + 1) * (yCount + 1)];//定义顶点
            for (int i = 0; i < yCount + 1; i++)
            {
                for (int j = 0; j < xCount + 1; j++)
                {
                    System.Drawing.Color color = bitmap.GetPixel((int)(j * cellWidth), (int)(i * cellHeight));
                    float height = float.Parse(color.R.ToString()) + float.Parse(color.G.ToString()) + float.Parse(color.B.ToString());
                    height /= 10;
                    vertices[j + i * (xCount + 1)].Position = new Vector3(j * cellWidth, height, i * cellHeight);
                    vertices[j + i * (xCount + 1)].Texcoord = new Vector2((float)j / (xCount + 1), (float)i / (yCount + 1));
                }
            }
            texture = TextureLoader.TextureLoader.CreateTextureFromDDS(device, @"C:\Users\yulanli\Desktop\TerrainForm\colorMapDDS.DDS");
            //创建待绘制图形的顶点索引
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
            //创建视锥体
            //创建摄像机
            CamTarget = new Vector3(bitmap.Width / 2, 0f, bitmap.Height / 2);
            view = Matrix.LookAtLH(
                CamPostion,//摄像机原点
                CamTarget,//摄像机观察目标点
                Vector3.UnitY);//当前世界的向上方向的向量，通常为（0,1,0），即这里的UnitY参数
            proj = Matrix.Identity;
            proj = Matrix.PerspectiveFovLH(
                (float)Math.PI / 4.0f,//用弧度制表示垂直视场角，这里是45°角
                aspectRatio,//纵横比
                0.3f,//到近平面的距离
                500.0f//到远平面的距离
                );
            var worldViewProj = Matrix.Multiply(proj, view);

            //使用上传堆来传递顶点缓冲区的数据
            /*--------------------------------------------------*
             * 不推荐使用上传堆来传递像顶点缓冲区这样的静态数据 *
             * 这里使用上载堆是为了代码的简洁性，并且还因为需要 *
             * 传递的资源很少                                   *
             *--------------------------------------------------*/
            var vertexBufferSize = Utilities.SizeOf(vertices);
            vertexBuffer = device.CreateCommittedResource(
                new HeapProperties(HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(vertexBufferSize),
                ResourceStates.GenericRead);

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
            vertexBufferView.StrideInBytes = Utilities.SizeOf<PositionTextured>();
            vertexBufferView.SizeInBytes = vertexBufferSize;

            
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

            //创建bundle
            bundle = device.CreateCommandList(
                0,
                CommandListType.Bundle,
                bundleAllocator,
                pipelineState);
            bundle.SetGraphicsRootSignature(rootSignature);
            bundle.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            bundle.SetVertexBuffer(0, vertexBufferView);
            bundle.SetIndexBuffer(indexBufferView);

            //bundle.DrawInstanced(
            //   vertices.Length,//VertexCountPerInstance,要绘制的顶点数
            //    1,//InstanceCount，要绘制的实例数，这里是1个
            //    0,//StartVertexLocation，第一个顶点的索引，这里是0
            //    0);//StartInstanceLocation，在从顶点缓冲区读取每个实例数据之前添加到每个索引的值

            bundle.DrawIndexedInstanced(
                indices.Length,//IndexCountPerInstance,要绘制的索引数
                1,//InstanceCount，要绘制的实例数，这里是1个
                0,//StartIndexLocation，第一个顶点的索引，这里是0
                0,//BaseVertexLocation,,从顶点缓冲区读取顶点之前添加到每个索引的值
                0);//StartInstanceLocation，在从顶点缓冲区读取每个实例数据之前添加到每个索引的值
            bundle.Close();

            //使用上传堆来传递常量缓冲区的数据
            /*--------------------------------------------------*
             * 不推荐使用上传堆来传递像垂直缓冲区这样的静态数据 *
             * 这里使用上载堆是为了代码的简洁性，并且还因为需要 *
             * 传递的资源很少                                   *
             *--------------------------------------------------*/
            constantBuffer = device.CreateCommittedResource(
                new HeapProperties(HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(1024 * 64),
                ResourceStates.GenericRead);

            //创建SRV视图
            var srvDesc = new ShaderResourceViewDescription();
            srvDesc.Texture2D.MostDetailedMip = 0;
            srvDesc.Texture2D.ResourceMinLODClamp = 0.0f;
            device.CreateShaderResourceView(texture, srvDesc, shaderRenderViewHeap.CPUDescriptorHandleForHeapStart);

            //创建常量缓冲区视图（CBV）
            var cbvDesc = new ConstantBufferViewDescription()
            {
                BufferLocation = constantBuffer.GPUVirtualAddress,
                SizeInBytes = (Utilities.SizeOf<ConstantBuffer>() + 255) & ~255
            };
            device.CreateConstantBufferView(
                cbvDesc,
                constantBufferViewHeap.CPUDescriptorHandleForHeapStart);

            //初始化并映射常量缓冲区
            /*--------------------------------------------------*
             * 直到应用程序关闭，我们才会取消映射，因此在资源的 *
             * 生命周期中保持映射是可以的                       *
             *------------------------------------------------- */
            constantBufferPointer = constantBuffer.Map(0);
            Utilities.Write(constantBufferPointer, ref worldViewProj);

            //创建同步对象
            //创建围栏
            fence = device.CreateFence(
                0,//围栏的初始值
                FenceFlags.None);//指定围栏的类型，None表示没有指定的类型
            fenceValue = 1;
            //创建用于帧同步的事件句柄
            fenceEvent = new AutoResetEvent(false);
        }

        //导入贴图和材质
        private void LoadTexturesAndMaterials()
        {
            

        }

        //填充命令列表
        private void PopulateCommandList()
        {
            //命令列表分配器只有当相关的命令列表在GPU上执行完成后才能重置，应用应当使用围栏来确定GPU的执行进度
            commandAllocator.Reset();

            //但是当在特定的命令列表上调用ExecuteCommandList()时，可以随时重置该命令列表，并且必须在此之前重新写入
            commandList.Reset(commandAllocator, pipelineState);

            //设置根签名布局
            commandList.SetGraphicsRootSignature(rootSignature);

            //设置描述符堆
            //更改与命令列表相关联的当前绑定的描述符堆
            commandList.SetDescriptorHeaps(
                1,//要绑定的描述符堆的数量
                new DescriptorHeap[]
                {
                    constantBufferViewHeap
                });//指向要在命令列表上设置的堆的对象的指针

            //为描述符表设置图形根签名
            commandList.SetGraphicsRootDescriptorTable(
                0,//用于绑定的符堆序号
                renderTargetViewHeap.GPUDescriptorHandleForHeapStart);//用于设置基本描述符的GPU_descriptor_handle对象
            //将纹理绑定至根参数0
            commandList.SetGraphicsRootDescriptorTable(
                0,
                shaderRenderViewHeap.GPUDescriptorHandleForHeapStart
                );

            //设置视口和裁剪矩形
            commandList.SetViewport(viewPort);
            commandList.SetScissorRectangles(scissorRectangle);

            //按照资源的用途指示其状态的改变
            commandList.ResourceBarrierTransition(
                renderTargets[frameIndex],
                ResourceStates.Present,
                ResourceStates.RenderTarget);

            CpuDescriptorHandle rtvHandle = renderTargetViewHeap.CPUDescriptorHandleForHeapStart;
            rtvHandle += frameIndex * rtvDescriptorSize;

            //为渲染目标和深度模板设置CPU描述符句柄
            commandList.SetRenderTargets(rtvHandle, null);

            //写入命令
            commandList.ClearRenderTargetView(rtvHandle, new Color4(0.4f, 0.0f, 0.4f, 1), 0, null);

            //执行bundle
            commandList.ExecuteBundle(bundle);

            //按照资源的用途指示其状态的改变
            commandList.ResourceBarrierTransition(
                renderTargets[frameIndex],
                ResourceStates.RenderTarget,
                ResourceStates.Present);

            commandList.Close();
        }



        //等待前面的命令列表执行完毕
        private void WaitForPreviousFrame()
        {
            /*----------------------------------------------------------------*
             * 等待此帧的命令列表执行完毕，当前的实现没有什么效率，也过于简单 *
             * 将在后面重新组织渲染部分的代码，以免在每一帧都需要等待         *
             *----------------------------------------------------------------*/
            int fence = fenceValue;
            commandQueue.Signal(this.fence, fence);
            fenceValue++;

            //等待前面的帧结束
            if (this.fence.CompletedValue < fence)
            {
                this.fence.SetEventOnCompletion(
                    fence,
                    fenceEvent.SafeWaitHandle.DangerousGetHandle());
                fenceEvent.WaitOne();
            }

            frameIndex = swapChain.CurrentBackBufferIndex;
        }

        public void Update()
        {

        }

        public void Render()
        {
            //将渲染场景所需的所有命令都记录到命令列表中
            PopulateCommandList();

            //执行命令列表
            commandQueue.ExecuteCommandList(commandList);

            //显示当前帧
            swapChain.Present(1, 0);

            //等待前一帧
            WaitForPreviousFrame();
        }

        //释放资源
        public void Dispose()
        {
            //等待GPU处理完所有的资源
            WaitForPreviousFrame();

            //释放所有资源
            foreach (var target in renderTargets)
            {
                target.Dispose();
            }
            commandAllocator.Dispose();
            bundleAllocator.Dispose();
            commandQueue.Dispose();
            rootSignature.Dispose();
            pipelineState.Dispose();
            vertexBuffer.Dispose();
            texture.Dispose();
            indexBuffer.Dispose();
            renderTargetViewHeap.Dispose();
            commandList.Dispose();
            bundle.Dispose();
            fence.Dispose();
            swapChain.Dispose();
            device.Dispose();
        }
        
    }
}


