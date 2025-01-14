module FVim.States

open common
open SkiaSharp
open log
open System.Reflection

[<Struct>]
type Request = 
    {
        method:     string
        parameters: obj[]
    }

[<Struct>]
type Response = 
    {
        result: Result<obj, obj>
    }

[<Struct>]
type Event =
| Request      of reqId: int32 * req: Request * handler: (int32 -> Response -> unit Async)
| Response     of rspId: int32 * rsp: Response
| Notification of nreq: Request
| Error        of emsg: string
| Crash        of ccode: int32
| Exit

let private _stateChangeEvent = Event<string>()
let private _appLifetime = Avalonia.Application.Current.ApplicationLifetime :?> Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime

// request handlers are explicitly registered, 1:1, with no broadcast.
let requestHandlers      = hashmap[]
// notification events are broadcasted to all subscribers.
let notificationEvents   = hashmap[]

let getNotificationEvent eventName =
    match notificationEvents.TryGetValue eventName with
    | true, ev -> ev
    | _ ->
    let ev = Event<obj[]>()
    notificationEvents.[eventName] <- ev
    ev

type LineHeightOption =
| Absolute of float
| Add of float
| Default

// clipboard
let mutable clipboard_lines: string[] = [||]
let mutable clipboard_regtype: string = ""

// cursor
let mutable cursor_smoothmove  = false
let mutable cursor_smoothblink = false

// font rendering
let mutable font_antialias     = true
let mutable font_drawBounds    = false
let mutable font_autohint      = false
let mutable font_subpixel      = true
let mutable font_lcdrender     = true
let mutable font_autosnap      = true
let mutable font_hintLevel     = SKPaintHinting.NoHinting
let mutable font_weight_normal = SKFontStyleWeight.Normal
let mutable font_weight_bold   = SKFontStyleWeight.Bold
let mutable font_lineheight    = LineHeightOption.Default

module private Helper =
    type Foo = A
    let _StatesModuleType = typeof<Foo>.DeclaringType.DeclaringType
    let SetProp name v =
        trace "states" "module name = %s" _StatesModuleType.FullName
        let propDesc = _StatesModuleType.GetProperty(name, BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic)
        if propDesc <> null then
            propDesc.SetValue(null, v)
        else
            error "states" "The property %s is not found" name
    let GetProp name =
        trace "states" "module name = %s" _StatesModuleType.FullName
        let propDesc = _StatesModuleType.GetProperty(name, BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic)
        if propDesc <> null then
            Some <| propDesc.GetValue(null)
        else
            error "states" "The property %s is not found" name
            None


let parseHintLevel (v: obj) = 
    match v with
    | String v ->
        match v.ToLower() with
        | "none" -> Some SKPaintHinting.NoHinting
        | "slight" -> Some SKPaintHinting.Slight
        | "normal" -> Some SKPaintHinting.Normal
        | "full" -> Some SKPaintHinting.Full
        | _ -> None
    | _ -> None

let parseFontWeight (v: obj) =
    match v with
    | Integer32 v -> Some(LanguagePrimitives.EnumOfValue v)
    | _ -> None

let parseLineHeightOption (v: obj) =
    match v with
    | String v ->
        if v.StartsWith("+") then
            Some(Add(float v.[1..]))
        elif v.StartsWith("-") then
            Some(Add(-float v.[1..]))
        elif v.ToLowerInvariant() = "default" then
            Some Default
        else
            Some(Absolute(float v))
    | _ -> None

let Shutdown code = _appLifetime.Shutdown code
let SetTitle title = _appLifetime.MainWindow.Title <- title

let msg_dispatch =
    function
    | Request(id, req, reply) -> 
        match requestHandlers.TryGetValue req.method with
        | true, method ->
            Async.Start(async { 
                try
                    let! rsp = method(req.parameters)
                    do! reply id rsp
                with
                | Failure msg -> error "rpc" "request %d(%s) failed: %s" id req.method msg
            })
        | _ -> error "rpc" "request handler [%s] not found" req.method

    | Notification(req) ->
        let event = getNotificationEvent req.method
        try event.Trigger req.parameters
        with | Failure msg -> error "rpc" "notification trigger [%s] failed: %s" req.method msg
    | Exit -> _appLifetime.Shutdown()
    | Crash code ->
        trace "model" "neovim crashed with code %d" code
        _appLifetime.Shutdown()
    | _ -> ()

module Register =
    let Request name fn = 
        requestHandlers.Add(name, fun objs ->
            try fn objs
            with x -> 
                error "Request" "exception thrown: %O" x
                async { return { result = Result.Error(box x) } })

    let Notify name (fn: obj[] -> unit) = 
        (getNotificationEvent name).Publish.Subscribe(fun objs -> 
            try fn objs
            with x -> error "Notify" "exception thrown: %A" <| x.ToString())

    let Watch name fn =
        _stateChangeEvent.Publish
        |> Observable.filter (fun x -> x = name)
        |> Observable.subscribe (fun _ -> fn())

    let Prop<'T> (parser: obj -> 'T option) (fullname: string) =
        let section = fullname.Split(".").[0]
        let fieldName = fullname.Replace(".", "_")
        Notify fullname (fun v ->
            match v with
            | [| v |] ->
                match parser(v) with
                | Some v -> 
                    Helper.SetProp fieldName v
                    _stateChangeEvent.Trigger section
                | None -> ()
            | _ -> ())
        |> ignore
        Request fullname (fun _ -> async { 
            let result = 
                match Helper.GetProp fieldName with
                | Some v -> Ok v
                | None -> Result.Error(box "not found")
            return { result=result }
        })

    let Bool = Prop<bool> (|Bool|_|)
    let String = Prop<string> (|String|_|)

