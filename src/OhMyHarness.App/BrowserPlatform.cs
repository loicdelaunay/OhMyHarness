using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    async Task<string> ExecuteBrowserScriptAsync(string script)
    {
        return await browser.ExecuteScriptAsync(script);
    }
    async Task<string> BrowserDevToolsAsync(string method,string parameters)
    {
#if WINDOWS
        return await browser.CoreWebView2.CallDevToolsProtocolMethodAsync(method,parameters);
#else
        var args = JsonNode.Parse(parameters) as JsonObject ?? throw new ArgumentException("Invalid browser command parameters.");
        if (method == "Runtime.evaluate")
        {
            var result = await browser.ExecuteScriptAsync(args["expression"]?.GetValue<string>() ?? "");
            return "{\"result\":{\"value\":" + (string.IsNullOrWhiteSpace(result) ? "null" : result) + "}}";
        }
        var payload = JsonSerializer.Serialize(args);
        var script = method switch
        {
            "Input.dispatchMouseEvent" => $$"""
                (() => {
                  const p = {{payload}}, el = document.elementFromPoint(p.x,p.y) || document.body;
                  if (!el) return false;
                  const button = p.button === 'right' ? 2 : p.button === 'middle' ? 1 : p.button === 'none' ? -1 : 0;
                  const options = {bubbles:true,cancelable:true,clientX:p.x,clientY:p.y,button,
                    buttons:p.buttons ?? (p.type==='mouseReleased'?0:(button===2?2:button===1?4:button===-1?0:1)),detail:p.clickCount||1};
                  if (p.type === 'mouseWheel') {
                    el.dispatchEvent(new WheelEvent('wheel',{...options,deltaX:p.deltaX||0,deltaY:p.deltaY||0}));
                    (el.scrollHeight > el.clientHeight ? el : window).scrollBy(p.deltaX||0,p.deltaY||0);
                  } else if (p.type === 'mouseMoved') {
                    if (typeof PointerEvent === 'function') el.dispatchEvent(new PointerEvent('pointermove',{...options,pointerId:1,pointerType:'mouse'}));
                    el.dispatchEvent(new MouseEvent('mousemove',options));
                  }
                  else {
                    const down = p.type === 'mousePressed';
                    if (typeof PointerEvent === 'function') el.dispatchEvent(new PointerEvent(down?'pointerdown':'pointerup',{...options,pointerId:1,pointerType:'mouse'}));
                    el.dispatchEvent(new MouseEvent(down?'mousedown':'mouseup',options));
                    if (!down && !p.drag && button === 0) el.click();
                    if (!down && !p.drag && button === 2) el.dispatchEvent(new MouseEvent('contextmenu',options));
                  }
                  return true;
                })()
                """,
            "Input.insertText" => $$"""
                (() => {
                  const p = {{payload}}, el = document.activeElement;
                  if (!el) return false;
                  if ('value' in el) {
                    const start = el.selectionStart ?? el.value.length, end = el.selectionEnd ?? start;
                    el.value = el.value.slice(0,start) + p.text + el.value.slice(end);
                    el.setSelectionRange?.(start+p.text.length,start+p.text.length);
                  } else if (el.isContentEditable) document.execCommand('insertText',false,p.text);
                  else return false;
                  el.dispatchEvent(new InputEvent('input',{bubbles:true,data:p.text,inputType:'insertText'}));
                  return true;
                })()
                """,
            "Input.dispatchKeyEvent" => $$"""
                (() => {
                  const p = {{payload}}, el = document.activeElement || document.body;
                  if (!el) return false;
                  const down = p.type === 'keyDown';
                  el.dispatchEvent(new KeyboardEvent(down?'keydown':'keyup',{
                    bubbles:true,cancelable:true,key:p.key,code:p.code,
                    altKey:!!(p.modifiers&1),ctrlKey:!!(p.modifiers&2),
                    metaKey:!!(p.modifiers&4),shiftKey:!!(p.modifiers&8)}));
                  if (down && p.text && 'value' in el) {
                    const start = el.selectionStart ?? el.value.length, end = el.selectionEnd ?? start;
                    el.value = el.value.slice(0,start) + p.text + el.value.slice(end);
                    el.setSelectionRange?.(start+p.text.length,start+p.text.length);
                    el.dispatchEvent(new InputEvent('input',{bubbles:true,data:p.text,inputType:'insertText'}));
                  }
                  return true;
                })()
                """,
            _ => throw new NotSupportedException("Embedded browser command unavailable: " + method)
        };
        if (await browser.ExecuteScriptAsync(script) != "true") throw new IOException("The embedded browser could not perform the requested interaction.");
        return "{}";
#endif
    }
}
