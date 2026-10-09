// DokiMicHelper: records push-to-talk clips for the Doki Doki Merchant mod on macOS.
// Slay the Spire 2 has no microphone permission of its own, so the mod launches this tiny app, which asks for
// access once (System Settings > Privacy & Security > Microphone) and talks to the mod over a localhost socket:
//   helper -> mod: 'Y' (mic allowed) or 'N' (refused), once, right after connecting
//   mod -> helper: 'S' start recording, 'E' end recording
//   helper -> mod: after 'E', a 4-byte little-endian length followed by a 16-bit mono WAV clip
// The helper quits when the socket closes (the shop closed or the game quit).
import AVFoundation
import Foundation

guard CommandLine.arguments.count > 1, let port = UInt16(CommandLine.arguments[1]) else {
    FileHandle.standardError.write("usage: DokiMicHelper <port>\n".data(using: .utf8)!)
    exit(2)
}

let fd = socket(AF_INET, SOCK_STREAM, 0)
var addr = sockaddr_in()
addr.sin_family = sa_family_t(AF_INET)
addr.sin_port = port.bigEndian
addr.sin_addr.s_addr = inet_addr("127.0.0.1")
let connected = withUnsafePointer(to: &addr) {
    $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { connect(fd, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) }
}
if connected != 0 { exit(1) }

func log(_ msg: String) {
    // Visible in Console.app (process DokiMicHelper) and on stderr when run from a terminal.
    NSLog("[DokiMicHelper] %@", msg)
}

func send(_ data: Data) {
    data.withUnsafeBytes { buf in
        var off = 0
        while off < buf.count {
            let n = write(fd, buf.baseAddress! + off, buf.count - off)
            if n <= 0 { exit(0) }
            off += n
        }
    }
}

// Ask for the microphone (shows the system prompt the first time).
let sem = DispatchSemaphore(value: 0)
var allowed = false
AVCaptureDevice.requestAccess(for: .audio) { ok in allowed = ok; sem.signal() }
sem.wait()
send(Data([allowed ? UInt8(ascii: "Y") : UInt8(ascii: "N")]))
if !allowed {
    // Stay connected so the mod keeps showing the explanation; quit when it hangs up.
    var b: UInt8 = 0
    while read(fd, &b, 1) > 0 {}
    exit(0)
}

let engine = AVAudioEngine()
let input = engine.inputNode
let format = input.inputFormat(forBus: 0)
let rate = Int(format.sampleRate)
log("input format: \(format)")
let lock = NSLock()
var recording = false
var samples = [Int16]()

input.installTap(onBus: 0, bufferSize: 2048, format: format) { buffer, _ in
    lock.lock(); defer { lock.unlock() }
    guard recording, let ch = buffer.floatChannelData else { return }
    let n = Int(buffer.frameLength)
    let channels = Int(format.channelCount)
    samples.reserveCapacity(samples.count + n)
    for i in 0..<n {
        var v: Float = 0
        for c in 0..<channels { v += ch[c][i] }
        v /= Float(channels)
        samples.append(Int16(max(-1, min(1, v)) * 32767))
    }
}

func wav(_ s: [Int16], rate: Int) -> Data {
    var d = Data()
    func u32(_ v: Int) { var x = UInt32(v).littleEndian; d.append(Data(bytes: &x, count: 4)) }
    func u16(_ v: Int) { var x = UInt16(v).littleEndian; d.append(Data(bytes: &x, count: 2)) }
    let bytes = s.count * 2
    d.append("RIFF".data(using: .ascii)!); u32(36 + bytes); d.append("WAVE".data(using: .ascii)!)
    d.append("fmt ".data(using: .ascii)!); u32(16); u16(1); u16(1); u32(rate); u32(rate * 2); u16(2); u16(16)
    d.append("data".data(using: .ascii)!); u32(bytes)
    s.withUnsafeBufferPointer { d.append(Data(buffer: $0)) }
    return d
}

// Keep the engine running for the whole shop visit: starting it on each key press would clip the first words.
do { try engine.start() } catch { log("engine.start failed: \(error)") }

var b: UInt8 = 0
while read(fd, &b, 1) > 0 {
    if b == UInt8(ascii: "S") {
        lock.lock(); samples.removeAll(); recording = true; lock.unlock()

    } else if b == UInt8(ascii: "E") {
        lock.lock(); recording = false; let clip = samples; samples.removeAll(); lock.unlock()
        log("clip: \(clip.count) samples at \(rate) Hz")
        let data = clip.count < rate / 5 ? Data() : wav(clip, rate: rate)
        var len = UInt32(data.count).littleEndian
        send(Data(bytes: &len, count: 4))
        if !data.isEmpty { send(data) }
    }
}
exit(0)
