"""Document model plus Markdown and PDF renderers for the TKG_PDA reference.

One content model feeds both outputs so the .md source and the .pdf can never
drift apart. Blocks are deliberately few and explicit; the content modules build
a list of them and the renderers walk it.

Block kinds:
  ("h", level, text)         section heading, auto-numbered (levels 1..4)
  ("p", text)                paragraph
  ("ul", [items])            bullet list
  ("ol", [items])            numbered list
  ("table", [headers], rows) table
  ("code", text, lang)       source excerpt / shell block
  ("pre", text)              monospace diagram (no syntax colouring)
  ("note", kind, text)       callout; kind in NOTE/WARN/FACT
  ("pagebreak",)
"""

import io
import os
import re

from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    BaseDocTemplate,
    Frame,
    KeepTogether,
    NextPageTemplate,
    PageBreak,
    PageTemplate,
    Paragraph,
    Preformatted,
    Spacer,
    Table,
    TableStyle,
)
from reportlab.platypus.tableofcontents import TableOfContents

FONT_DIR = "C:/Windows/Fonts"
BODY, BODY_B, BODY_I, MONO, MONO_B = "TkgBody", "TkgBody-Bold", "TkgBody-Italic", "TkgMono", "TkgMono-Bold"


def register_fonts() -> None:
    pdfmetrics.registerFont(TTFont(BODY, os.path.join(FONT_DIR, "arial.ttf")))
    pdfmetrics.registerFont(TTFont(BODY_B, os.path.join(FONT_DIR, "arialbd.ttf")))
    pdfmetrics.registerFont(TTFont(BODY_I, os.path.join(FONT_DIR, "ariali.ttf")))
    pdfmetrics.registerFont(TTFont(MONO, os.path.join(FONT_DIR, "consola.ttf")))
    pdfmetrics.registerFont(TTFont(MONO_B, os.path.join(FONT_DIR, "consolab.ttf")))
    pdfmetrics.registerFontFamily(BODY, normal=BODY, bold=BODY_B, italic=BODY_I, boldItalic=BODY_B)


# --------------------------------------------------------------------------
# helpers
# --------------------------------------------------------------------------

def esc(text: str) -> str:
    """Escapes for ReportLab mini-HTML, then re-enables our inline markers."""
    out = text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
    out = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", out)
    out = re.sub(r"`(.+?)`", r'<font name="%s">\1</font>' % MONO, out)
    return out


_TAG = re.compile(r"<[^>]*>")
# Only break when a real space does not already follow: ReportLab collapses
# consecutive whitespace, so a zero-size space next to a real one eats it.
_BREAKABLE = re.compile(r"([/\\._:])(?!\s)")
_ZWBREAK = r"\1<font size=0> </font>"


def wrap_path(text: str) -> str:
    """Lets long file paths and identifiers break inside table cells.

    Runs on text that esc() has already turned into mini-HTML, so it must skip
    anything inside a tag: a break inserted into "</font>" would corrupt the
    markup and ReportLab would draw the tag literally.
    """
    out = []
    pos = 0
    for m in _TAG.finditer(text):
        out.append(_BREAKABLE.sub(_ZWBREAK, text[pos:m.start()]))
        out.append(m.group(0))
        pos = m.end()
    out.append(_BREAKABLE.sub(_ZWBREAK, text[pos:]))
    return "".join(out)


class Numberer:
    """Assigns 1 / 1.2 / 1.2.3 style numbers to headings in document order."""

    def __init__(self) -> None:
        self.counters = [0, 0, 0, 0]

    def next(self, level: int) -> str:
        idx = level - 1
        self.counters[idx] += 1
        for deeper in range(idx + 1, 4):
            self.counters[deeper] = 0
        return ".".join(str(c) for c in self.counters[: idx + 1])


# --------------------------------------------------------------------------
# Markdown renderer
# --------------------------------------------------------------------------

def render_markdown(blocks, meta) -> str:
    out = [
        f"# {meta['title']}\n",
        f"> {meta['subtitle']}\n",
        "",
        f"- **Dokümante edilen branch:** `{meta['branch']}`",
        f"- **Dokümante edilen commit:** `{meta['commit']}`",
        f"- **Doküman üretim tarihi:** {meta['date']}",
        "",
        "---",
        "",
    ]
    num = Numberer()
    for b in blocks:
        kind = b[0]
        if kind == "h":
            n = num.next(b[1])
            out.append(f"\n{'#' * min(b[1] + 1, 6)} {n} {b[2]}\n")
        elif kind == "p":
            out.append(b[1] + "\n")
        elif kind == "ul":
            out.extend(f"- {i}" for i in b[1])
            out.append("")
        elif kind == "ol":
            out.extend(f"{i + 1}. {v}" for i, v in enumerate(b[1]))
            out.append("")
        elif kind == "table":
            heads, rows = b[1], b[2]
            out.append("| " + " | ".join(heads) + " |")
            out.append("|" + "|".join(["---"] * len(heads)) + "|")
            for r in rows:
                cells = [str(c).replace("\n", " ").replace("|", "\\|") for c in r]
                out.append("| " + " | ".join(cells) + " |")
            out.append("")
        elif kind == "code":
            out.append(f"```{b[2] if len(b) > 2 else ''}\n{b[1]}\n```\n")
        elif kind == "pre":
            out.append(f"```text\n{b[1]}\n```\n")
        elif kind == "note":
            label = {"NOTE": "NOT", "WARN": "UYARI", "FACT": "KANIT"}.get(b[1], b[1])
            out.append(f"> **{label}:** {b[2]}\n")
        elif kind == "pagebreak":
            out.append("\n---\n")
    return "\n".join(out)


# --------------------------------------------------------------------------
# PDF renderer
# --------------------------------------------------------------------------

class RefDoc(BaseDocTemplate):
    def __init__(self, path, meta, **kw):
        super().__init__(path, pagesize=A4, **kw)
        self.meta = meta
        frame = Frame(18 * mm, 18 * mm, A4[0] - 36 * mm, A4[1] - 36 * mm, id="body")
        self.addPageTemplates([
            PageTemplate(id="cover", frames=[frame], onPage=self._cover_page),
            PageTemplate(id="body", frames=[frame], onPage=self._body_page),
        ])

    def _cover_page(self, canv, doc):
        canv.saveState()
        canv.setFillColor(colors.HexColor("#0f2740"))
        canv.rect(0, A4[1] - 78 * mm, A4[0], 78 * mm, stroke=0, fill=1)
        canv.restoreState()

    def _body_page(self, canv, doc):
        canv.saveState()
        canv.setFont(BODY, 7.5)
        canv.setFillColor(colors.HexColor("#5a6b7d"))
        canv.drawString(18 * mm, A4[1] - 12 * mm, self.meta["title"])
        canv.drawRightString(A4[0] - 18 * mm, A4[1] - 12 * mm, f"commit {self.meta['commit'][:12]}")
        canv.setStrokeColor(colors.HexColor("#d5dde5"))
        canv.line(18 * mm, A4[1] - 14 * mm, A4[0] - 18 * mm, A4[1] - 14 * mm)
        canv.line(18 * mm, 14 * mm, A4[0] - 18 * mm, 14 * mm)
        canv.drawString(18 * mm, 10 * mm, self.meta["footer"])
        canv.drawRightString(A4[0] - 18 * mm, 10 * mm, f"Sayfa {canv.getPageNumber()}")
        canv.restoreState()

    def afterFlowable(self, flowable):
        # Feeds the automatic table of contents with real page numbers.
        style = getattr(flowable, "style", None)
        name = getattr(style, "name", "")
        if name.startswith("TocH"):
            level = int(name[4]) - 1
            text = flowable.getPlainText()
            key = f"toc{id(flowable)}"
            self.canv.bookmarkPage(key)
            self.notify("TOCEntry", (level, text, self.page, key))


def _styles():
    ss = getSampleStyleSheet()
    s = {}
    s["body"] = ParagraphStyle("body", parent=ss["Normal"], fontName=BODY, fontSize=9.2,
                               leading=13.4, spaceAfter=5, alignment=TA_LEFT)
    s["cover_title"] = ParagraphStyle("ct", fontName=BODY_B, fontSize=25, leading=30,
                                      textColor=colors.white)
    s["cover_sub"] = ParagraphStyle("cs", fontName=BODY, fontSize=11.5, leading=16,
                                    textColor=colors.HexColor("#c6d6e6"))
    s["cover_meta"] = ParagraphStyle("cm", fontName=MONO, fontSize=9, leading=15,
                                     textColor=colors.HexColor("#12354f"))
    for lvl, size, colr, before in ((1, 16.5, "#0f2740", 13), (2, 12.6, "#14405f", 10),
                                    (3, 10.6, "#1c4f72", 8), (4, 9.6, "#2a5f83", 6)):
        s[f"h{lvl}"] = ParagraphStyle(f"TocH{lvl}", fontName=BODY_B, fontSize=size,
                                      leading=size * 1.28, textColor=colors.HexColor(colr),
                                      spaceBefore=before, spaceAfter=size * 0.32,
                                      keepWithNext=1)
    s["code"] = ParagraphStyle("code", fontName=MONO, fontSize=7.3, leading=9.2,
                               textColor=colors.HexColor("#17242f"))
    s["cell"] = ParagraphStyle("cell", fontName=BODY, fontSize=7.7, leading=9.8)
    s["cellh"] = ParagraphStyle("cellh", fontName=BODY_B, fontSize=7.7, leading=9.8,
                                textColor=colors.white)
    s["note"] = ParagraphStyle("note", parent=s["body"], fontSize=8.7, leading=12.4,
                               leftIndent=7, spaceBefore=3, spaceAfter=6)
    return s


NOTE_COLORS = {
    "NOTE": ("#eef4fb", "#3f7cb6", "NOT"),
    "WARN": ("#fff6e8", "#c07a17", "UYARI"),
    "FACT": ("#eef8f0", "#2f8348", "KANIT"),
}


def render_pdf(blocks, meta, path):
    register_fonts()
    st = _styles()
    doc = RefDoc(path, meta, title=meta["title"], author="TKG_PDA",
                 subject="Teknik referans", creator="TKG_PDA docs/generator")
    story = []
    avail = A4[0] - 36 * mm

    # ---- cover -----------------------------------------------------------
    story.append(Spacer(1, 16 * mm))
    story.append(Paragraph(esc(meta["title"]), st["cover_title"]))
    story.append(Spacer(1, 4 * mm))
    story.append(Paragraph(esc(meta["subtitle"]), st["cover_sub"]))
    story.append(Spacer(1, 46 * mm))
    cover_rows = [
        ["Dokümante edilen branch", meta["branch"]],
        ["Dokümante edilen commit", meta["commit"]],
        ["Baz alınan main commit", meta["base"]],
        ["Doküman üretim tarihi", meta["date"]],
        ["Kapsanan kaynak dosya", meta["coverage_line"]],
    ]
    t = Table([[Paragraph(esc(a), st["cell"]), Paragraph(wrap_path(esc(b)), st["cover_meta"])]
               for a, b in cover_rows], colWidths=[52 * mm, avail - 52 * mm])
    t.setStyle(TableStyle([
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
        ("LINEBELOW", (0, 0), (-1, -2), 0.4, colors.HexColor("#dfe6ee")),
    ]))
    story.append(t)
    story.append(Spacer(1, 10 * mm))
    story.append(Paragraph(esc(meta["cover_note"]), st["body"]))
    story.append(NextPageTemplate("body"))
    story.append(PageBreak())

    # ---- table of contents ----------------------------------------------
    story.append(Paragraph("İçindekiler", st["h1"]))
    toc = TableOfContents()
    toc.levelStyles = [
        ParagraphStyle("t0", fontName=BODY_B, fontSize=9.4, leading=14, spaceBefore=4),
        ParagraphStyle("t1", fontName=BODY, fontSize=8.6, leading=12, leftIndent=10),
        ParagraphStyle("t2", fontName=BODY, fontSize=8.0, leading=11, leftIndent=22,
                       textColor=colors.HexColor("#425568")),
        ParagraphStyle("t3", fontName=BODY, fontSize=7.6, leading=10, leftIndent=34,
                       textColor=colors.HexColor("#5d6f80")),
    ]
    story.append(toc)
    story.append(PageBreak())

    # ---- body ------------------------------------------------------------
    num = Numberer()
    for b in blocks:
        kind = b[0]
        if kind == "h":
            n = num.next(b[1])
            story.append(Paragraph(f"{n}&nbsp;&nbsp;{esc(b[2])}", st[f"h{b[1]}"]))
        elif kind == "p":
            story.append(Paragraph(esc(b[1]), st["body"]))
        elif kind in ("ul", "ol"):
            for i, item in enumerate(b[1]):
                bullet = "•" if kind == "ul" else f"{i + 1}."
                story.append(Paragraph(
                    f'<font name="{MONO}">{bullet}</font>&nbsp;&nbsp;{esc(item)}',
                    ParagraphStyle("li", parent=st["body"], leftIndent=11, spaceAfter=2)))
            story.append(Spacer(1, 3))
        elif kind == "table":
            story.append(_table(b[1], b[2], st, avail))
        elif kind in ("code", "pre"):
            story.append(_code(b[1], st, avail))
        elif kind == "note":
            bg, line, label = NOTE_COLORS.get(b[1], NOTE_COLORS["NOTE"])
            inner = Paragraph(f"<b>{label}:</b> {esc(b[2])}", st["note"])
            nt = Table([[inner]], colWidths=[avail])
            nt.setStyle(TableStyle([
                ("BACKGROUND", (0, 0), (-1, -1), colors.HexColor(bg)),
                ("LINEBEFORE", (0, 0), (0, -1), 2.2, colors.HexColor(line)),
                ("LEFTPADDING", (0, 0), (-1, -1), 7),
                ("RIGHTPADDING", (0, 0), (-1, -1), 7),
                ("TOPPADDING", (0, 0), (-1, -1), 5),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
            ]))
            story.append(nt)
            story.append(Spacer(1, 4))
        elif kind == "pagebreak":
            story.append(PageBreak())

    doc.multiBuild(story)
    return doc.page


def _table(heads, rows, st, avail):
    ncol = len(heads)
    # Weight the first column wider: it usually carries a path or identifier.
    if ncol == 2:
        widths = [avail * 0.34, avail * 0.66]
    elif ncol == 3:
        widths = [avail * 0.26, avail * 0.22, avail * 0.52]
    else:
        first = avail * 0.22
        widths = [first] + [(avail - first) / (ncol - 1)] * (ncol - 1)
    data = [[Paragraph(esc(str(h)), st["cellh"]) for h in heads]]
    for r in rows:
        data.append([Paragraph(wrap_path(esc(str(c))), st["cell"]) for c in r])
    t = Table(data, colWidths=widths, repeatRows=1, hAlign="LEFT")
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#1c4f72")),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("GRID", (0, 0), (-1, -1), 0.35, colors.HexColor("#c9d4de")),
        ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#f4f7fa")]),
        ("LEFTPADDING", (0, 0), (-1, -1), 4),
        ("RIGHTPADDING", (0, 0), (-1, -1), 4),
        ("TOPPADDING", (0, 0), (-1, -1), 3),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 3),
    ]))
    return t


def _code(text, st, avail):
    # Hard-wrap so no code line can overflow the page width.
    lines = []
    for raw in text.split("\n"):
        raw = raw.replace("\t", "    ")
        while len(raw) > 108:
            lines.append(raw[:108])
            raw = "    " + raw[108:]
        lines.append(raw)
    body = Preformatted("\n".join(lines), st["code"])
    t = Table([[body]], colWidths=[avail])
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), colors.HexColor("#f5f7f9")),
        ("BOX", (0, 0), (-1, -1), 0.4, colors.HexColor("#d5dde5")),
        ("LEFTPADDING", (0, 0), (-1, -1), 6),
        ("RIGHTPADDING", (0, 0), (-1, -1), 6),
        ("TOPPADDING", (0, 0), (-1, -1), 5),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
    ]))
    return t
