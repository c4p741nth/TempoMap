# ทบทวนฟีเจอร์ TempoMap เทียบ ArrowVortex / Tempora

ตรวจวันที่ 2026-10-25 จาก TempoMap working tree หลัง E54 บน base 567dcc8
รอบวิจัยนี้ไม่ได้แก้ source ของแอป ต่อมาผู้ใช้อนุมัติ **F01–F18 ทั้งหมด** เมื่อ 2026-10-25
สถานะการลงโค้ดและผลตรวจอยู่ใน [EDITOR-UPGRADE](EDITOR-UPGRADE.md); A6 cleanup ถูก policy ของ Codex ปฏิเสธ แล้วผู้ใช้รันสคริปต์เองเสร็จเมื่อ 2026-10-26

ผมให้น้ำหนักกับการดู/ฟัง/แก้ timing ได้คล่องและกู้งานได้ก่อนเพิ่ม detector ใหม่
ตัวที่ผมเริ่มก่อนคือ **F01–F06**; F07 น่าทดลองถ้าคุณใช้การนับห้อง/จังหวะทำเพลงสดบ่อย
ขนาดงานเป็นความเห็นจากโครงสร้างปัจจุบัน ไม่ใช่กำหนดเวลาหรือผล benchmark

## ขอบเขตที่ตรวจจริง

- ArrowVortex **2017-02-25**: ไฟล์ local ที่คุณให้, SHA-256 `4cb96f91946fdb0773d510e7caaabd2cecd0b424859780cbf221e1fd95e389ca`
  ใช้ REA 6.7.0 + Ghidra 12.1.4 สำรวจ PE x86, 5,601 functions / 2,289 defined strings
  ตาม xref ของ Find BPM ถึง dialog และอ่าน handler ที่ผูกไว้ ไม่ได้ย้อนทุกฟังก์ชันหรือ benchmark detector
- [เว็บผู้พัฒนา ArrowVortex](https://arrowvortex.ddrnl.com/) แยกรุ่น legacy จาก fork ที่เปิด source ตั้งแต่ปี 2025
  อ่าน source ของ public **beta** ประกอบและระบุรุ่นไว้; ฟีเจอร์ของรุ่นใหม่ไม่ถูกนับเป็นสิ่งที่รุ่น 2017 มีโดยอัตโนมัติ
- Tempora: README และ source บน **master** ได้แก่ timing model, settings, save/autosave, metronome,
  playback, spectrogram และ exporter. ไม่ได้ build/เปิด Tempora เพื่อวัด UX หรือความหน่วงจริง
- TempoMap: อ่าน implementation ของ view model, waveform, playback, interaction, save/close และ key handlers
  คำว่า “ยังไม่พบ” หมายถึงเส้นทางที่ตรวจ ไม่ใช่การยืนยันจากผลค้นหาว่างเพียงอย่างเดียว

## สิ่งที่ TempoMap มีแล้ว: ใช้/ปรับทางเข้าก่อนเพิ่มซ้ำ

| เรื่อง | สถานะที่ตรวจ |
| --- | --- |
| จับ BPM/offset, หลาย BPM, drift, range/section | มี Auto-time และเครื่องมือเจาะช่วงแล้ว; ยังไม่วัดความแม่นกับ ArrowVortex ในรอบนี้ |
| ลาก timing / ยืดกริด | มีลากเส้นแดง และ Ctrl+ลาก beat เพื่อปรับ BPM ใน TimingInteraction; F07 เสนอ workflow A/B/N เพิ่มจากนี้ |
| แก้ half/double tempo | มีตัวคูณกรอกเองและ preset; F05 เสนอแสดงคำตอบทางเลือก ไม่ใช่เพิ่มปุ่ม ×2 ซ้ำ |
| วิเคราะห์จาก spacing / สร้าง ramp | มี StepTiming.FromSpacing และ Fill ramp; spacing เดาปริมาณ beats และจำกัดช่วงสั้น ไม่ใช่ผู้ใช้กรอกจำนวน beats โดยตรง |
| Undo/Redo | มี snapshot รวม normalization mode; ไม่มีเหตุผลต้องเขียนระบบ Undo ใหม่เพื่อเลียนแบบโปรแกรมอื่น |
| SV/Kiai และ normalize | มี Manual SV ตามที่คุณเลือกและ optional Auto normalize; F06 คือเพิ่มภาพบน timeline |
| ความเร็ว/volume/metronome | มี 0.25–1.0, volume แยก และเสียง accent ห้อง; F11 คือ shortcut ช่วยเปรียบเทียบ |
| Beat subdivisions | BeatSnap มีตัวเลือก triplet/เลขคี่อยู่แล้ว; metronome ปัจจุบันยังนัด click เป็น whole beats |
| สำรองไฟล์ | Save สำรอง `.osu.bak` อยู่แล้ว; F01 เพิ่ม draft/recovery ก่อนกด Save |
| แปลงเสียง Ogg q6 | มีแล้วใน AudioImport; ไม่ต้องเพิ่มการแปลงแบบ ArrowVortex ซ้ำ |

## ตัวเลือกสำหรับเพิ่ม

เครื่องหมายอ้างอิงในคอลัมน์ท้ายกดไปยัง source ด้านล่างได้ “ข้อเสนอ” คือแนวคิดที่ผมเสนอเองจากช่องว่างปัจจุบัน
ไม่ได้อ้างว่าเป็นฟีเจอร์ที่ ArrowVortex/Tempora มีทั้งหมด

### คุ้มกับ workflow ปัจจุบันก่อน

| ID / ฟีเจอร์ | TempoMap ตอนนี้ | ตัวอย่างที่จะใช้ | ขนาด / ที่มา |
| --- | --- | --- | --- |
| **F01 Autosave + กู้งาน + เตือนงานยังไม่เซฟ** | มี .osu.bak ตอน Save แต่ยังไม่พบ autosave/dirty guard ใน Close/Open ที่ตรวจ | แก้เส้นมานานแล้วปิดหรือเปิดเพลงใหม่ผิด: กู้ draft กลับมาได้; backup ตอน Save ไม่ครอบคลุมกรณีนี้ | กลาง · [T4] / [TM1] |
| **F02 Loop ช่วงที่เลือก + pre-roll** | มี range สำหรับวิเคราะห์ แต่ transport ไม่มี loop ของช่วง | เลือก chorus แล้วฟังวนพร้อม metronome; ย้อนก่อนต้นช่วงเล็กน้อยเพื่อจับจังหวะได้ทัน | กลาง · ข้อเสนอ workflow / [TM2] |
| **F03 Filtered waveform + onset overlay** | แสดง raw waveform; ใช้ onset วิเคราะห์ภายใน แต่ยังไม่มีชั้นภาพแยกให้ดู | เพลงเบสหนาทับเสียงกลอง: เปิด high-pass/low-pass และเทียบ onset กับกริด; ไม่ต้องแก้ไฟล์เสียง | กลาง · [AV4], [AV7] / [TM3] |
| **F04 พรีวิวกริดและฟัง A/B ก่อน Apply** | มีข้อความ/list proposal และ preview ตอนลาก; waveform/เสียงปัจจุบันยังตามเส้นที่ใช้จริง | สลับกริดเดิมกับผล Auto-time เพื่อฟังก่อนยืนยัน; Apply ค่อยสร้าง undo หนึ่งขั้น | กลาง · ข้อเสนอจากช่องว่าง / [TM4] |
| **F05 BPM หลายตัวเลือกพร้อมคะแนนเปรียบเทียบ** | single proposal มี BPM เดียว; multi proposal คือหลาย section ไม่ใช่หลายคำตอบของช่วงเดียว | เห็น 143 กับ 286 แล้วเลือกฟังตัวเลือกได้; ถ้าผู้ใช้พิมพ์ล็อก BPM ต้องคงค่าที่ล็อก | กลาง–ใหญ่ · [AV2], [AV3] / [TM5] |
| **F06 Lane ของ SV/Kiai บน timeline** | แก้ green/SV/Kiai ได้ในตาราง แต่ WaveformControl ยังไม่วาด green/effect regions | เห็น chorus เปิด/ปิด Kiai ตรงไหน และ SV 1.75 ช่วงใด โดยไม่ไล่หาในตาราง | กลาง · ข้อเสนอสำหรับ osu! / [TM3] |

### เลือกต่อเมื่อเข้ากับวิธีทำงาน

| ID / ฟีเจอร์ | TempoMap ตอนนี้ | ตัวอย่างที่จะใช้ | ขนาด / ที่มา |
| --- | --- | --- | --- |
| **F07 A/B anchors + ระบุจำนวน beats เพื่อหา BPM** | มี Ctrl+drag stretch และ BPM from spacing แล้ว; ยังไม่มี workflow ระบุ A/B/N beats ชัด ๆ | รู้ว่าจุด A ถึง B ยาว 8 beats: ให้กรอกจำนวนแล้วหา BPM โดยไม่ต้องเดาค่าเดิมให้ใกล้ก่อน | กลาง · [T2] / [TM6] |
| **F08 Overview ทั้งเพลง + bookmarks ชื่อท่อน** | มี seek bar, ตารางและ marks; ยังไม่มีแผนที่ทั้งเพลงพร้อมชื่อ verse/chorus | คลิก bookmark chorus หรือช่วงที่ต้องแก้ โดยไม่ซูมออกแล้วหาตำแหน่งใหม่ | กลาง · [AV1], [AV6] / [TM3], [TM7] |
| **F09 หน้าทบทวน Check sections / กดไปปัญหาถัดไป** | มี ShiftMs/Ratio/Weak และแถบสีแล้ว; รายละเอียดอยู่ status ซึ่งแสดงเพียงรายการต้น ๆ | ไล่ฟัง section ที่น่าสงสัยทีละจุด พร้อมเหตุผลและก่อน/หลัง; กราฟ drift เป็นขั้นต่อยอด | เล็ก–กลาง · ข้อเสนอเพิ่มทางเข้าของเดิม / [TM7] |
| **F10 ปรับ shortcut ได้เอง** | คีย์ส่วนใหญ่กำหนดตรงใน handler; ไม่มี keymap editor/config ที่ตรวจพบ | คนใช้มือซ้ายหรือ keyboard layout อื่นย้าย Add/Move/Loop ไปปุ่มที่เอื้อมง่าย | กลาง · [AV1], local shortcuts.txt / [TM8] |
| **F11 ปุ่มสลับ speed สองค่า + กดค้าง mute metronome** | มี 0.25/0.5/0.75/1.0 และ M toggle อยู่แล้ว; ยังไม่มี quick A/B rate / momentary mute | กดสลับ 1.0 ↔ 0.5 ขณะฟัง หรือกดค้างเพื่อฟังเพลงล้วนโดยไม่เสียระดับ volume เดิม | เล็ก · [T5], [T6] / [TM2], [TM8] |
| **F12 Spectrogram** | waveform ปัจจุบันแสดง amplitude อย่างเดียว | เพลงที่ transient ถูกเสียงยาวกลบ: ดูย่านความถี่ช่วยหา attack ของ kick/snare; ต้องทำ cache/zoom ให้เหมาะ | ใหญ่ · [T3], [T8] / [TM3] |
| **F17 Progress/Cancel และ audio import diagnostics** | มี busy/status และ import conversion; ไม่พบปุ่มยกเลิก analysis หรือหน้าตรวจ ffmpeg | เพลงยาวหรือเครื่องอื่นเปิดไฟล์ไม่ผ่าน: รู้ว่ากำลังทำขั้นไหน ใช้ tool จากไหน และยกเลิกงานอย่างถูกต้อง | กลาง · ข้อเสนอความพร้อมใช้งาน / [TM9] |

### เฉพาะงาน / เลือกไว้ทีหลังได้

| ID / ฟีเจอร์ | TempoMap ตอนนี้ | ตัวอย่างที่จะใช้ | ขนาด / ที่มา |
| --- | --- | --- | --- |
| **F13 Project/session file แยกจาก .osu** | เซฟ .osu และ settings ได้ แต่ไม่เก็บ draft proposal/bookmarks/view ต่อโปรเจกต์ | ปิดงานครึ่งทางแล้วกลับมาที่ช่วงเดิมพร้อมตัวเลือกวิเคราะห์; เริ่มจาก recovery F01 ก่อนก็ได้ | กลาง–ใหญ่ · [T4] / [TM1] |
| **F14 มุมมองหลายแถวตามห้อง + ruler ห้อง/beat** | มี waveform ตามเวลาและกริด/เมตร; ยังไม่มี multi-row measure layout | ทำเพลงวงสดทีละห้องและเทียบท่อนที่คล้ายกัน โดยไม่เลื่อนแนวนอนยาว ๆ; ควรเป็น view เสริม | ใหญ่ · [T1], [T3] / [TM3] |
| **F15 Export .osz พร้อมเพลง** | มี Save .osu และ Copy timing; ไม่พบ export package .osz ในเส้นทาง UI ที่ตรวจ | เริ่ม timing จาก audio แล้วส่งแพ็กเข้า osu! ได้ครั้งเดียว; workflow เดิมที่วาง clipboard อาจไม่จำเป็น | กลาง · [T7] / [TM1] |
| **F16 Tap tempo เป็นจุดตั้งต้น** | ยังไม่พบทางรับการเคาะ BPM; มี locked BPM และ fine nudge แล้ว | เคาะตามเพลงสดเพื่อประมาณ BPM ก่อนใช้ Fit offset/anchors; ไม่ใช้การเคาะเป็นหลักฐาน offset แม่นยำ | เล็ก · ข้อเสนอเสริม / [TM5], [TM8] |
| **F18 Copy offset เป็น profile/ตัวเลือกชัดเจน** | ปัจจุบัน Copy ใช้ -33 ms ตามที่ผู้ใช้เลือก; Save/internal paste คง source time | ใช้หลายเครื่องหรือแชร์แอป: เลือก source timing หรือ calibration ของเครื่องนั้นได้ โดยไม่เปลี่ยนค่าเดิมของคุณเอง | กลาง · [T3], [T7] ใช้เป็นข้อเปรียบเทียบเท่านั้น / [TM10] |

## ข้อแตกต่างที่ต้องออกแบบให้ชัดก่อนเลือก

- **F05:** เปอร์เซ็นต์ใน public beta ของ ArrowVortex มาจาก fitness ของตัวเลือกหารผลรวม
  ไม่ใช่ความน่าจะเป็นว่า BPM ถูก ควรเขียนว่า “คะแนนเทียบตัวเลือก” และฟังเปรียบเทียบได้
  ไม่ให้ผลวิเคราะห์ใหม่ทับ BPM ที่คุณพิมพ์ล็อกไว้อีก [AV3]
- **F07:** Tempora ผูก audio offset กับ measure position แล้วหา BPM ระหว่าง anchors [T2]
  ต่างจาก osu! red point ที่ระบุ BPM ของช่วงถัดไป. ตัวอย่าง A=10 s, B=14 s, 8 beats ให้ BPM = 8×60÷4 = 120
  ควรเริ่มเป็น tool เล็ก ๆ พร้อม preview/Undo ก่อนคิดเปลี่ยน data model ทั้งโปรแกรม
- **F12 กับ F03:** spectrogram ช่วยแยกความถี่ ส่วน filtered waveform/onset overlay มักง่ายกว่าในการดู attack
  ผมเริ่ม F03 ก่อน แล้วค่อยเลือก F12 ถ้าคุณใช้เพลงที่ภาพ waveform ยังอ่านยาก
- **F14 / time signature:** TempoMap มี Meter เช่น 3/4 อยู่แล้ว แต่ data model ยังเป็นจำนวน beats ต่อห้อง
  ไม่มี denominator แยกแบบ 6/8 ของ Tempora. การเพิ่ม ruler/6/8 จริงต้องกำหนดวิธี export ลง osu! ให้ชัดก่อน [T2]
- **F18:** Tempora มี export offset อีกค่าหนึ่งตาม convention ของโปรแกรมนั้น [T3], [T7]
  ไม่ควรนำตัวเลขนั้นมาแทน -33 ms ที่คุณเลือกจากการฟัง/วัดใน session เดิมโดยตรง

## สิ่งที่ผมยังไม่แนะนำให้ยกมาทั้งชุด

- Note editor, generator, difficulty rating, Dancing Bot และ noteskins ของ ArrowVortex:
  เหมาะกับโปรแกรมเขียน chart; เพิ่มเข้ามาตอนนี้ทำให้ TempoMap ต้องดูแลเครื่องมือคนละกลุ่ม [AV1], [AV6]
- StepMania stops/warps/speed segments หรือ export Beat Saber/Guitar Game:
  ควรเพิ่มเมื่อคุณต้องใช้ format นั้นจริง ไม่ใช่เพียงเพราะอีกโปรแกรมมี [T4], [T7]
- แทน detector เดิมด้วย ArrowVortex ทันที: ต้องมี benchmark บนเพลงเดียวกันและรักษา parity/guard sets ก่อน
  ฟีเจอร์ UI ของอีกโปรแกรมไม่ได้เป็นหลักฐานว่า detector ของมันแม่นกว่าของเรา
- เขียนโหมด anchor/multi-row มาแทน editor เดิมทั้งหมด: ควรทดลองเป็นเครื่องมือหรือ view เสริมก่อน
  เพื่อคง workflow red/green, Manual SV และ BPM lock ที่คุณเพิ่งปรับไว้

## รายการให้คุณติ๊กเลือก

ติ๊กหมายถึงอยากพิจารณาเพิ่ม ไม่ได้แปลว่าลงมือทำแล้ว คุณตอบเป็น ID เช่น `F02 F03 F06` ได้

- [x] **F01** Autosave + กู้งาน + เตือนงานยังไม่เซฟ
- [x] **F02** Loop ช่วงที่เลือก + pre-roll
- [x] **F03** Filtered waveform + onset overlay
- [x] **F04** พรีวิวกริดและฟัง A/B ก่อน Apply
- [x] **F05** BPM หลายตัวเลือกพร้อมคะแนนเปรียบเทียบ
- [x] **F06** Lane ของ SV/Kiai บน timeline
- [x] **F07** A/B anchors + ระบุจำนวน beats เพื่อหา BPM
- [x] **F08** Overview ทั้งเพลง + bookmarks ชื่อท่อน
- [x] **F09** หน้าทบทวน Check sections / กดไปปัญหาถัดไป
- [x] **F10** ปรับ shortcut ได้เอง
- [x] **F11** ปุ่มสลับ speed สองค่า + กดค้าง mute metronome
- [x] **F12** Spectrogram
- [x] **F13** Project/session file แยกจาก .osu
- [x] **F14** มุมมองหลายแถวตามห้อง + ruler ห้อง/beat
- [x] **F15** Export .osz พร้อมเพลง
- [x] **F16** Tap tempo เป็นจุดตั้งต้น
- [x] **F17** Progress/Cancel และ audio import diagnostics
- [x] **F18** Copy offset เป็น profile/ตัวเลือกชัดเจน

## หลักฐานการใช้ REA กับตัว 2017

1. วิเคราะห์ `ArrowVortex.exe` ได้จริงหลังตั้ง `JAVA_HOME` แบบ absolute ไป JDK 21 เฉพาะ process
   ไม่รัน rea setup หรือแก้ config ของ editor/MCP ทั้งเครื่อง
2. REA พบ Find BPM string ที่ `0x4ce6dc`; xref ไป instruction `0x41d13f`
3. Decompile instruction นั้นได้ function `FUN_0041ce5f` ซึ่งสร้าง dialog:
   Initial BPM / Music offset / ปุ่มเลื่อนครึ่งและหนึ่ง beat / รายการ BPM / Apply / Find
4. binding ของ Find ชี้ `FUN_0041d46b`; อ่าน handler แล้วมีเส้นทางเลือกช่วงและเรียก detector factory
   แต่ยังไม่ยืนยันพารามิเตอร์/คุณภาพ detector ทั้ง pipeline จาก handler นี้เพียงอย่างเดียว
5. พบข้อความ Overlay filtered waveform / Filter type / Apply filter / Tempo breakdown ใน binary
   และตรวจทับกับ settings ของรุ่น local/เอกสาร legacy/source. ไม่ใช้ `StretchNoLoop` เป็นหลักฐานว่าโปรแกรมมี loop

Evidence และ decompiler output อยู่เฉพาะ `.dotnet-home/feature-review-20261025/` ที่ gitignore
ไม่ใส่ third-party code/decompile หรือไฟล์เพลงคนอื่นลง repo. ไฟล์ต้นฉบับตรวจ SHA256 ซ้ำหลังวิเคราะห์

## การนำแนวคิดมาใช้

รายการนี้เป็นการเทียบพฤติกรรมและออกแบบ requirement ของเราเอง
Tempora ระบุ [CC BY-NC-ND 4.0](https://github.com/teamkongehund/Tempora/blob/master/LICENSE.txt)
และ maintained ArrowVortex fork ระบุ [GPL-3.0-or-later](https://github.com/uvcat7/ArrowVortex#license)
จึงไม่ถือว่าเปิดดู source แล้วสามารถยก implementation เข้า repo MIT ได้อัตโนมัติ
ยังไม่มีการนำ code ของทั้งสองโปรแกรมมาใช้ใน TempoMap ในรอบนี้

## แหล่งอ้างอิง

- **AV1** — [ArrowVortex legacy overview](https://arrowvortex.ddrnl.com/overview.html)
- **AV2** — [ArrowVortex legacy basic guide](https://arrowvortex.ddrnl.com/guide.html)
- **AV3** — [ArrowVortex public beta: AdjustSync.cpp](https://github.com/uvcat7/ArrowVortex/blob/beta/src/Dialogs/AdjustSync.cpp)
- **AV4** — [ArrowVortex public beta: WaveformSettings.cpp](https://github.com/uvcat7/ArrowVortex/blob/beta/src/Dialogs/WaveformSettings.cpp)
- **AV6** — [ArrowVortex maintained fork: legacy/new feature distinction](https://github.com/uvcat7/ArrowVortex)
- **AV7** — [ArrowVortex public beta: Waveform.cpp](https://github.com/uvcat7/ArrowVortex/blob/beta/src/Editor/Waveform.cpp)
- **T1** — [Tempora README](https://github.com/teamkongehund/Tempora)
- **T2** — [Tempora TimingPoint: audio-time / measure-position anchors](https://github.com/teamkongehund/Tempora/blob/master/Classes/TimingClasses/TimingPoint.cs)
- **T3** — [Tempora Settings: rows, overlap, spectrogram and export options](https://github.com/teamkongehund/Tempora/blob/master/Classes/Singletons/Settings.cs)
- **T4** — [Tempora ProjectFileManager: project and autosave](https://github.com/teamkongehund/Tempora/blob/master/Classes/Singletons/ProjectFileManager.cs)
- **T5** — [Tempora MusicPlayer: alternate playback rate](https://github.com/teamkongehund/Tempora/blob/master/Classes/Audio/MusicPlayer.cs)
- **T6** — [Tempora Metronome: temporary mute and subdivision](https://github.com/teamkongehund/Tempora/blob/master/Classes/Audio/Metronome.cs)
- **T7** — [Tempora OsuExporter: .osz and export convention](https://github.com/teamkongehund/Tempora/blob/master/Classes/DataHelpers/OsuExporter.cs)
- **T8** — [Tempora SpectrogramContext](https://github.com/teamkongehund/Tempora/blob/master/Classes/Visual/AudioDisplay/SpectrogramContext.cs)
- **TM1** — [TempoMap Close/Open/Save paths](../src/TimingAnalyzer.App/MainWindow.axaml.cs)
- **TM2** — [TempoMap playback contract and speeds](../src/TimingAnalyzer.Editor/IPlaybackEngine.cs)
- **TM3** — [TempoMap waveform Render](../src/TimingAnalyzer.App/WaveformControl.cs)
- **TM4** — [TempoMap DisplayRedLines, proposals and SetTiming](../src/TimingAnalyzer.Editor/EditorViewModel.cs)
- **TM5** — [TempoMap single-BPM proposal contract](../src/TimingAnalyzer.Editor/AutoTimingProposal.cs)
- **TM6** — [TempoMap Ctrl+drag/beat stretch](../src/TimingAnalyzer.Editor/TimingInteraction.cs)
- **TM7** — [TempoMap section-mark metrics](../src/TimingAnalyzer.Editor/TempoScale.cs)
- **TM8** — [TempoMap key handlers and help](../src/TimingAnalyzer.App/MainWindow.axaml.cs)
- **TM9** — [TempoMap audio conversion](../src/TimingAnalyzer.Editor/AudioImport.cs)
- **TM10** — [TempoMap calibrated Copy export](../src/TimingAnalyzer.Editor/OsuTimingExport.cs)

[AV1]: https://arrowvortex.ddrnl.com/overview.html
[AV2]: https://arrowvortex.ddrnl.com/guide.html
[AV3]: https://github.com/uvcat7/ArrowVortex/blob/beta/src/Dialogs/AdjustSync.cpp
[AV4]: https://github.com/uvcat7/ArrowVortex/blob/beta/src/Dialogs/WaveformSettings.cpp
[AV6]: https://github.com/uvcat7/ArrowVortex
[AV7]: https://github.com/uvcat7/ArrowVortex/blob/beta/src/Editor/Waveform.cpp
[T1]: https://github.com/teamkongehund/Tempora
[T2]: https://github.com/teamkongehund/Tempora/blob/master/Classes/TimingClasses/TimingPoint.cs
[T3]: https://github.com/teamkongehund/Tempora/blob/master/Classes/Singletons/Settings.cs
[T4]: https://github.com/teamkongehund/Tempora/blob/master/Classes/Singletons/ProjectFileManager.cs
[T5]: https://github.com/teamkongehund/Tempora/blob/master/Classes/Audio/MusicPlayer.cs
[T6]: https://github.com/teamkongehund/Tempora/blob/master/Classes/Audio/Metronome.cs
[T7]: https://github.com/teamkongehund/Tempora/blob/master/Classes/DataHelpers/OsuExporter.cs
[T8]: https://github.com/teamkongehund/Tempora/blob/master/Classes/Visual/AudioDisplay/SpectrogramContext.cs
[TM1]: ../src/TimingAnalyzer.App/MainWindow.axaml.cs
[TM2]: ../src/TimingAnalyzer.Editor/IPlaybackEngine.cs
[TM3]: ../src/TimingAnalyzer.App/WaveformControl.cs
[TM4]: ../src/TimingAnalyzer.Editor/EditorViewModel.cs
[TM5]: ../src/TimingAnalyzer.Editor/AutoTimingProposal.cs
[TM6]: ../src/TimingAnalyzer.Editor/TimingInteraction.cs
[TM7]: ../src/TimingAnalyzer.Editor/TempoScale.cs
[TM8]: ../src/TimingAnalyzer.App/MainWindow.axaml.cs
[TM9]: ../src/TimingAnalyzer.Editor/AudioImport.cs
[TM10]: ../src/TimingAnalyzer.Editor/OsuTimingExport.cs
