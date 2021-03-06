﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="LineGeometryModel3D.cs" company="Helix Toolkit">
//   Copyright (c) 2014 Helix Toolkit contributors
// </copyright>
// <summary>
//
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace HelixToolkit.Wpf.SharpDX
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Windows;

    using global::SharpDX;

    using global::SharpDX.Direct3D;

    using global::SharpDX.Direct3D11;

    using global::SharpDX.DXGI;

    using HelixToolkit.Wpf.SharpDX.Extensions;
    using HelixToolkit.Wpf.SharpDX.Utilities;

    using Color = global::SharpDX.Color;

    using Buffer = global::SharpDX.Direct3D11.Buffer;
    using System.Runtime.CompilerServices;

    public class LineGeometryModel3D : GeometryModel3D
    {
        private LinesVertex[] vertexArrayBuffer = null;
        protected InputLayout vertexLayout;
        protected Buffer vertexBuffer;
        protected Buffer indexBuffer;
        protected Buffer instanceBuffer;
        protected EffectTechnique effectTechnique;
        protected EffectTransformVariables effectTransforms;
        protected EffectVectorVariable vViewport, vLineParams; // vFrustum, 
        //private DepthStencilState depthStencilState;
        //private LineGeometry3D geometry;
        protected EffectScalarVariable bHasInstances;
        protected bool hasInstances = false;
        protected bool isChanged = true;

        public override int VertexSizeInBytes
        {
            get
            {
                return LinesVertex.SizeInBytes;
            }
        }

        [TypeConverter(typeof(ColorConverter))]
        public Color Color
        {
            get { return (Color)this.GetValue(ColorProperty); }
            set { this.SetValue(ColorProperty, value); }
        }

        public static readonly DependencyProperty ColorProperty =
            DependencyProperty.Register("Color", typeof(Color), typeof(LineGeometryModel3D), new UIPropertyMetadata(Color.Black, (o, e) => ((LineGeometryModel3D)o).OnColorChanged()));

        public double Thickness
        {
            get { return (double)this.GetValue(ThicknessProperty); }
            set { this.SetValue(ThicknessProperty, value); }
        }

        public static readonly DependencyProperty ThicknessProperty =
            DependencyProperty.Register("Thickness", typeof(double), typeof(LineGeometryModel3D), new UIPropertyMetadata(1.0));

        public double Smoothness
        {
            get { return (double)this.GetValue(SmoothnessProperty); }
            set { this.SetValue(SmoothnessProperty, value); }
        }

        public static readonly DependencyProperty SmoothnessProperty =
            DependencyProperty.Register("Smoothness", typeof(double), typeof(LineGeometryModel3D), new UIPropertyMetadata(0.0));

        public IList<Matrix> Instances
        {
            get { return (IList<Matrix>)this.GetValue(InstancesProperty); }
            set { this.SetValue(InstancesProperty, value); }
        }

        public static readonly DependencyProperty InstancesProperty =
            DependencyProperty.Register("Instances", typeof(IList<Matrix>), typeof(LineGeometryModel3D), new UIPropertyMetadata(null, InstancesChanged));

        public double HitTestThickness
        {
            get { return (double)this.GetValue(HitTestThicknessProperty); }
            set { this.SetValue(HitTestThicknessProperty, value); }
        }

        public static readonly DependencyProperty HitTestThicknessProperty =
            DependencyProperty.Register("HitTestThickness", typeof(double), typeof(LineGeometryModel3D), new UIPropertyMetadata(1.0));

        protected static void InstancesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var model = (LineGeometryModel3D)d;
            model.hasInstances = model.Instances != null && model.Instances.Any();
            model.isChanged = true;
        }

        public override bool HitTest(Ray rayWS, ref List<HitTestResult> hits)
        {
            LineGeometry3D lineGeometry3D;
            Viewport3DX viewport;

            if (this.Visibility == Visibility.Collapsed ||
                this.IsHitTestVisible == false ||
                (viewport = FindVisualAncestor<Viewport3DX>(this.renderHost as DependencyObject)) == null ||
                (lineGeometry3D = this.Geometry as LineGeometry3D) == null)
            {
                return false;
            }

            var result = new HitTestResult { IsValid = false, Distance = double.MaxValue };
            var lastDist = double.MaxValue;
            var index = 0;
            foreach (var line in lineGeometry3D.Lines)
            {
                var t0 = Vector3.TransformCoordinate(line.P0, this.ModelMatrix);
                var t1 = Vector3.TransformCoordinate(line.P1, this.ModelMatrix);
                Vector3 sp, tp;
                float sc, tc;
                var distance = LineBuilder.GetRayToLineDistance(rayWS, t0, t1, out sp, out tp, out sc, out tc);
                var svpm = viewport.GetScreenViewProjectionMatrix();
                Vector4 sp4;
                Vector4 tp4;
                Vector3.Transform(ref sp, ref svpm, out sp4);
                Vector3.Transform(ref tp, ref svpm, out tp4);
                var sp3 = sp4.ToVector3();
                var tp3 = tp4.ToVector3();
                var tv2 = new Vector2(tp3.X - sp3.X, tp3.Y - sp3.Y);
                var dist = tv2.Length();
                if (dist < lastDist && dist <= this.HitTestThickness)
                {
                    lastDist = dist;
                    result.PointHit = sp.ToPoint3D();
                    result.NormalAtHit = (sp - tp).ToVector3D(); // not normalized to get length
                    result.Distance = distance;
                    result.ModelHit = this;
                    result.IsValid = true;
                    result.Tag = index; // ToDo: LineHitTag with additional info
                }

                index++;
            }

            if (result.IsValid)
            {
                hits.Add(result);
            }

            return result.IsValid;
        }

        protected override void OnRasterStateChanged()
        {
            Disposer.RemoveAndDispose(ref this.rasterState);
            if (!IsAttached) { return; }
            // --- set up rasterizer states
            var rasterStateDesc = new RasterizerStateDescription()
            {
                FillMode = FillMode,
                CullMode = CullMode.None,
                DepthBias = DepthBias,
                DepthBiasClamp = -1000,
                SlopeScaledDepthBias = -2,
                IsDepthClipEnabled = true,
                IsFrontCounterClockwise = false,

                IsMultisampleEnabled = IsMultisampleEnabled,
                //IsAntialiasedLineEnabled = true, // Intel HD 3000 doesn't like this (#10051) and it's not needed
                IsScissorEnabled = IsThrowingShadow ? false : IsScissorEnabled
            };

            try { this.rasterState = new RasterizerState(this.Device, rasterStateDesc); }
            catch (System.Exception)
            {
            }
        }

        private void OnColorChanged()
        {
            if(IsAttached)
                CreateVertexBuffer();
        }

        protected override void OnGeometryPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            base.OnGeometryPropertyChanged(sender, e);
            if (sender is LineGeometry3D)
            {
                if (e.PropertyName.Equals(nameof(LineGeometry3D.Positions)))
                {
                    OnUpdateVertexBuffer(CreateLinesVertexArray);
                }
                else if (e.PropertyName.Equals(nameof(LineGeometry3D.Colors)))
                {
                    OnUpdateVertexBuffer(CreateLinesVertexArray);
                }
                else if (e.PropertyName.Equals(nameof(LineGeometry3D.Indices)) || e.PropertyName.Equals(Geometry3D.TriangleBuffer))
                {
                    Disposer.RemoveAndDispose(ref this.indexBuffer);
                    this.indexBuffer = Device.CreateBuffer(BindFlags.IndexBuffer, sizeof(int), this.Geometry.Indices.Array);
                    InvalidateRender();
                }
                else if (e.PropertyName.Equals(Geometry3D.VertexBuffer))
                {
                    OnUpdateVertexBuffer(CreateLinesVertexArray);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OnUpdateVertexBuffer(Func<LinesVertex[]> updateFunction)
        {
            CreateVertexBuffer();
            this.InvalidateRender();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CreateVertexBuffer()
        {
            var geometry = Geometry as LineGeometry3D;
            if (geometry != null && geometry.Positions != null)
            {
                Disposer.RemoveAndDispose(ref vertexBuffer);
                // --- set up buffers            
                var data = this.CreateLinesVertexArray();
                if (data != null)
                {
                    this.vertexBuffer = Device.CreateBuffer(BindFlags.VertexBuffer, VertexSizeInBytes, data, geometry.Positions.Count);
                }
            }
        }

        protected override RenderTechnique SetRenderTechnique(IRenderHost host)
        {
            return host.RenderTechniquesManager.RenderTechniques[DefaultRenderTechniqueNames.Lines];
        }

        protected override bool OnAttach(IRenderHost host)
        {
            // --- attach                        
            if (!base.OnAttach(host))
            {
                return false;
            }

            if (renderHost.RenderTechnique == renderHost.RenderTechniquesManager.RenderTechniques.Get(DeferredRenderTechniqueNames.Deferred) ||
                renderHost.RenderTechnique == renderHost.RenderTechniquesManager.RenderTechniques.Get(DeferredRenderTechniqueNames.GBuffer))
                return false;

            // --- get device
            vertexLayout = renderHost.EffectsManager.GetLayout(renderTechnique);
            effectTechnique = effect.GetTechniqueByName(renderTechnique.Name);

            effectTransforms = new EffectTransformVariables(effect);
            
            // --- get geometry
            var geometry = Geometry as LineGeometry3D;

            // -- set geometry if given
            if (geometry != null)
            {
                // --- set up buffers            
                CreateVertexBuffer();
                // --- set up indexbuffer
                indexBuffer = Device.CreateBuffer(BindFlags.IndexBuffer, sizeof(int), geometry.Indices.Array);
            }
            else
            {
                throw new ArgumentException("Geometry must be LineGeometry3D");
            }
          
            bHasInstances = effect.GetVariableByName("bHasInstances").AsScalar();

            // --- set up const variables
            vViewport = effect.GetVariableByName("vViewport").AsVector();
            //this.vFrustum = effect.GetVariableByName("vFrustum").AsVector();
            vLineParams = effect.GetVariableByName("vLineParams").AsVector();

            // --- set effect per object const vars
            var lineParams = new Vector4((float)Thickness, (float)Smoothness, 0, 0);
            vLineParams.Set(lineParams);

            // === debug hack
            //{
            //    var texDiffuseMapView = ShaderResourceView.FromFile(device, @"G:\Projects\Deformation Project\FrameworkWPF2012\Externals\HelixToolkit-SharpDX\Source\Examples\SharpDX.Wpf\LightingDemo\TextureCheckerboard2.jpg");
            //    var texDiffuseMap = effect.GetVariableByName("texDiffuseMap").AsShaderResource();
            //    texDiffuseMap.SetResource(texDiffuseMapView);                
            //}

            // --- flush
            //Device.ImmediateContext.Flush();
            return true;
        }        

        /// <summary>
        /// 
        /// </summary>
        protected override void OnDetach()
        {
            Disposer.RemoveAndDispose(ref this.vertexBuffer);
            Disposer.RemoveAndDispose(ref this.indexBuffer);
            Disposer.RemoveAndDispose(ref this.instanceBuffer);
            //Disposer.RemoveAndDispose(ref this.vFrustum);
            Disposer.RemoveAndDispose(ref this.vViewport);
            Disposer.RemoveAndDispose(ref this.vLineParams);            
            Disposer.RemoveAndDispose(ref this.rasterState);
            //Disposer.RemoveAndDispose(ref this.depthStencilState);
            Disposer.RemoveAndDispose(ref this.bHasInstances);

            this.renderTechnique = null;
            this.effectTechnique = null;
            this.vertexLayout = null;

            base.OnDetach();
        }

        protected override bool CanRender(RenderContext context)
        {
            if (base.CanRender(context))
            {
                if (renderHost.RenderTechnique == renderHost.RenderTechniquesManager.RenderTechniques.Get(DeferredRenderTechniqueNames.Deferred) ||
                    renderHost.RenderTechnique == renderHost.RenderTechniquesManager.RenderTechniques.Get(DeferredRenderTechniqueNames.GBuffer))
                {
                    return false;
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        protected override void OnRender(RenderContext renderContext)
        {
            // --- since these values are changed only per window resize, we set them only once here
            //if (this.isResized || renderContext.Camera != this.lastCamera)
            {
                //this.isResized = false;
                //this.lastCamera = renderContext.Camera;

                if (renderContext.Camera is ProjectionCamera)
                {
                    var c = renderContext.Camera as ProjectionCamera;
                    // viewport: W,H,0,0   
                    var viewport = new Vector4((float)renderContext.Canvas.ActualWidth, (float)renderContext.Canvas.ActualHeight, 0, 0);
                    var ar = viewport.X / viewport.Y;
                    this.vViewport.Set(ref viewport);

                    // Actually, we don't really need vFrustum because we already know the depth of the projected line.
                    //var fov = 100.0; // this is a fake value, since the line shader does not use it!
                    //var zn = c.NearPlaneDistance > 0 ? c.NearPlaneDistance : 0.1;
                    //var zf = c.FarPlaneDistance + 0.0;
                    // frustum: FOV,AR,N,F
                    //var frustum = new Vector4((float)fov, (float)ar, (float)zn, (float)zf);
                    //this.vFrustum.Set(ref frustum);
                }
            }
            // --- set transform paramerers             
            var worldMatrix = this.modelMatrix * renderContext.worldMatrix;
            this.effectTransforms.mWorld.SetMatrix(ref worldMatrix);

            // --- set effect per object const vars
            var lineParams = new Vector4((float)this.Thickness, (float)this.Smoothness, 0, 0);
            this.vLineParams.Set(lineParams);
            
            // --- set context
            renderContext.DeviceContext.InputAssembler.InputLayout = this.vertexLayout;
            renderContext.DeviceContext.InputAssembler.SetIndexBuffer(this.indexBuffer, Format.R32_UInt, 0);
            renderContext.DeviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;

            this.bHasInstances.Set(this.hasInstances);

            // --- set rasterstate            
            renderContext.DeviceContext.Rasterizer.State = this.rasterState;

            if (this.hasInstances)
            {
                // --- update instance buffer
                if (this.isChanged)
                {
                    if(instanceBuffer == null || instanceBuffer.Description.SizeInBytes < Matrix.SizeInBytes * this.Instances.Count)
                    {
                        Disposer.RemoveAndDispose(ref instanceBuffer);
                        this.instanceBuffer = Buffer.Create(this.Device, this.Instances.ToArray(), new BufferDescription(Matrix.SizeInBytes * this.Instances.Count, ResourceUsage.Dynamic, BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0));
                    }
                    else
                    {
                        DataStream stream;
                        renderContext.DeviceContext.MapSubresource(this.instanceBuffer, MapMode.WriteDiscard, global::SharpDX.Direct3D11.MapFlags.None, out stream);
                        stream.Position = 0;
                        stream.WriteRange(this.Instances.ToArray(), 0, this.Instances.Count);
                        renderContext.DeviceContext.UnmapSubresource(this.instanceBuffer, 0);
                        stream.Dispose();
                    }
                    this.isChanged = false;
                }

                // --- INSTANCING: need to set 2 buffers            
                renderContext.DeviceContext.InputAssembler.SetVertexBuffers(0, new[] 
                {
                    new VertexBufferBinding(this.vertexBuffer, VertexSizeInBytes, 0),
                    new VertexBufferBinding(this.instanceBuffer, Matrix.SizeInBytes, 0),
                });

                // --- render the geometry
                for (int i = 0; i < this.effectTechnique.Description.PassCount; i++)
                {
                    this.effectTechnique.GetPassByIndex(i).Apply(renderContext.DeviceContext);
                    renderContext.DeviceContext.DrawIndexedInstanced(this.Geometry.Indices.Count, this.Instances.Count, 0, 0, 0);
                }
                this.bHasInstances.Set(false);
            }
            else
            {
                // --- bind buffer                
                renderContext.DeviceContext.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(this.vertexBuffer, VertexSizeInBytes, 0));

                // --- render the geometry
                this.effectTechnique.GetPassByIndex(0).Apply(renderContext.DeviceContext);
                renderContext.DeviceContext.DrawIndexed(this.Geometry.Indices.Count, 0, 0);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public override void Dispose()
        {
            this.Detach();
        }

        /// <summary>
        /// Creates a <see cref="T:LinesVertex[]"/>.
        /// </summary>
        private LinesVertex[] CreateLinesVertexArray()
        {
            var positions = this.Geometry.Positions.Array;
            var vertexCount = this.Geometry.Positions.Count;
            var color = this.Color;
            if (!ReuseVertexArrayBuffer || vertexArrayBuffer == null || vertexArrayBuffer.Length < vertexCount)
                vertexArrayBuffer = new LinesVertex[vertexCount];

            if (this.Geometry.Colors != null && this.Geometry.Colors.Any())
            {
                var colors = this.Geometry.Colors;

                for (var i = 0; i < vertexCount; i++)
                {
                    vertexArrayBuffer[i].Position = new Vector4(positions[i], 1f);
                    vertexArrayBuffer[i].Color = color * colors[i];
                }
            }
            else
            {
                for (var i = 0; i < vertexCount; i++)
                {
                    vertexArrayBuffer[i].Position = new Vector4(positions[i], 1f);
                    vertexArrayBuffer[i].Color = color;
                }
            }

            return vertexArrayBuffer;
        }
    }
}
