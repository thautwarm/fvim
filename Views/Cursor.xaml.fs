﻿namespace FVim

open log
open ui
open common
open neovim.def

open Avalonia
open Avalonia.Animation
open Avalonia.Controls
open Avalonia.Data
open Avalonia.Markup.Xaml
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Skia
open Avalonia.Threading
open Avalonia.VisualTree
open SkiaSharp
open System
open System.Collections.Generic
open System.Reactive.Disposables
open System.Reactive.Linq
open Avalonia.Visuals.Media.Imaging
open System.Runtime.InteropServices

type Cursor() as this =
    inherit ViewBase<CursorViewModel>()

    let mutable cursor_timer: IDisposable = null
    let mutable bgbrush: SolidColorBrush  = SolidColorBrush(Colors.Black)
    let mutable fgbrush: SolidColorBrush  = SolidColorBrush(Colors.White)
    let mutable spbrush: SolidColorBrush  = SolidColorBrush(Colors.Red)
    let mutable cursor_fb = AllocateFramebuffer (20.0) (20.0) 1.0
    let mutable cursor_fb_vm = CursorViewModel(Some -1)
    let mutable cursor_fb_s = 1.0
    let mutable render_queued = false

    let ensure_fb() =
        let s = this.GetVisualRoot().RenderScaling
        if (cursor_fb_vm.VisualChecksum(),cursor_fb_s) <> (this.ViewModel.VisualChecksum(),s) then
            cursor_fb_vm <- this.ViewModel.Clone()
            cursor_fb_s <- s
            cursor_fb.Dispose()
            cursor_fb <- AllocateFramebuffer (cursor_fb_vm.Width + 50.0) (cursor_fb_vm.Height + 50.0) s
            true
        else false

    let fgpaint = new SKPaint()
    let bgpaint = new SKPaint()
    let sppaint = new SKPaint()

    let cursorTimerRun action time =
        if cursor_timer <> null then
            cursor_timer.Dispose()
            cursor_timer <- null
        if time > 0 then
            cursor_timer <- DispatcherTimer.RunOnce(Action(action), TimeSpan.FromMilliseconds(float time), DispatcherPriority.Render)

    let showCursor (v: bool) =
        let opacity = 
            if v && this.ViewModel.enabled && this.ViewModel.ingrid
            then 1.0
            else 0.0
        this.Opacity <- opacity
        
    let rec blinkon() =
        showCursor true
        cursorTimerRun blinkoff this.ViewModel.blinkon
    and blinkoff() = 
        showCursor false
        cursorTimerRun blinkon this.ViewModel.blinkoff

    let cursorConfig id =
        trace "cursor" "render tick %A" id
        if Object.ReferenceEquals(this.ViewModel, null) 
        then ()
        else
            (* update the settings *)
            if this.ViewModel.fg <> fgbrush.Color then
                fgbrush <- SolidColorBrush(this.ViewModel.fg)
            if this.ViewModel.bg <> bgbrush.Color then
                bgbrush <- SolidColorBrush(this.ViewModel.bg)
            if this.ViewModel.sp <> spbrush.Color then
                spbrush <- SolidColorBrush(this.ViewModel.sp)
            (* reconfigure the cursor *)
            showCursor true
            cursorTimerRun blinkon this.ViewModel.blinkwait
            if not render_queued then
                render_queued <- true
                this.InvalidateVisual()

    let setCursorAnimation() =
        let transitions = Transitions()
        if States.cursor_smoothblink then 
            let blink_transition = DoubleTransition()
            blink_transition.Property <- Cursor.OpacityProperty
            blink_transition.Duration <- TimeSpan.FromMilliseconds(150.0)
            blink_transition.Easing   <- Easings.LinearEasing()
            transitions.Add(blink_transition)
        if States.cursor_smoothmove then
            let x_transition = DoubleTransition()
            x_transition.Property <- Canvas.LeftProperty
            x_transition.Duration <- TimeSpan.FromMilliseconds(80.0)
            x_transition.Easing   <- Easings.CubicEaseOut()
            let y_transition = DoubleTransition()
            y_transition.Property <- Canvas.TopProperty
            y_transition.Duration <- TimeSpan.FromMilliseconds(80.0)
            y_transition.Easing   <- Easings.CubicEaseOut()
            transitions.Add(x_transition)
            transitions.Add(y_transition)
        this.SetValue(Cursor.TransitionsProperty, transitions)

    do
        this.Watch [
            this.OnRenderTick cursorConfig
            States.Register.Watch "cursor" setCursorAnimation
        ] 
        AvaloniaXamlLoader.Load(this)

    override this.Render(ctx) =
        //trace "cursor" "render begin"

        let cellw p = min (double(p) / 100.0 * this.Width) 1.0
        let cellh p = min (double(p) / 100.0 * this.Height) 5.0

        let scale = this.GetVisualRoot().RenderScaling

        match this.ViewModel.shape, this.ViewModel.cellPercentage with
        | CursorShape.Block, _ ->
            let _, typeface = GetTypeface(this.ViewModel.text, this.ViewModel.italic, this.ViewModel.bold, this.ViewModel.typeface, this.ViewModel.wtypeface)
            SetForegroundBrush(fgpaint, this.ViewModel.fg, typeface, this.ViewModel.fontSize)
            bgpaint.Color <- this.ViewModel.bg.ToSKColor()
            sppaint.Color <- this.ViewModel.sp.ToSKColor()
            let bounds = Rect(this.Bounds.Size)

            try
                match ctx.PlatformImpl with
                | :? ISkiaDrawingContextImpl ->
                    // immediate
                    SetOpacity fgpaint this.Opacity
                    SetOpacity bgpaint this.Opacity
                    SetOpacity sppaint this.Opacity
                    RenderText(ctx.PlatformImpl, bounds, scale, fgpaint, bgpaint, sppaint, this.ViewModel.underline, this.ViewModel.undercurl, this.ViewModel.text, ValueNone)
                | _ ->
                    // deferred
                    let redraw = ensure_fb()
                    if redraw then
                        let dc = cursor_fb.CreateDrawingContext(null)
                        dc.PushClip(bounds)
                        RenderText(dc, bounds, scale, fgpaint, bgpaint, sppaint, this.ViewModel.underline, this.ViewModel.undercurl, this.ViewModel.text, ValueNone)
                        dc.PopClip()
                        dc.Dispose()
                    ctx.DrawImage(cursor_fb, 1.0, Rect(0.0, 0.0, bounds.Width * scale, bounds.Height * scale), bounds)
            with
            | ex -> trace "cursor" "render exception: %s" <| ex.ToString()
        | CursorShape.Horizontal, p ->
            let h = (cellh p)
            let region = Rect(0.0, this.Height - h, this.Width, h)
            ctx.FillRectangle(SolidColorBrush(this.ViewModel.bg), region)
        | CursorShape.Vertical, p ->
            let region = Rect(0.0, 0.0, cellw p, this.Height)
            ctx.FillRectangle(SolidColorBrush(this.ViewModel.bg), region)

        render_queued <- false

