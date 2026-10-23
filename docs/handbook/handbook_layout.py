"""Layout of the EF Core Plugin Handbook (reportlab + pygments).

Provides fonts, paragraph styles, syntax-highlighted code blocks, callout boxes, tables,
flow diagrams, headings with table of contents and the document template. The part
scripts in parts/ (German) and parts_en/ (English) only use the helpers defined here.
"""
import os
import re

# ---------------------------------------------------------------- Language (de / en)
TEXTS = {
    "de": {"important": "Wichtig", "tip": "Tipp", "note": "Hinweis",
           "page": "Seite {page} von {total}", "part": "TEIL"},
    "en": {"important": "Important", "tip": "Tip", "note": "Note",
           "page": "Page {page} of {total}", "part": "PART"},
}
_LANG = ["de"]


def set_language(lang):
    if lang not in TEXTS:
        raise ValueError(f"Unknown language '{lang}' (de, en)")
    _LANG[0] = lang


def T(key):
    return TEXTS[_LANG[0]][key]
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm
from reportlab.lib import colors
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.enums import TA_LEFT
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.fonts import addMapping
from reportlab.pdfgen import canvas as rl_canvas
from reportlab.platypus import (BaseDocTemplate, PageTemplate, Frame, Paragraph, Spacer, Table,
                                TableStyle, KeepTogether, CondPageBreak, Flowable, PageBreak)
from reportlab.platypus.tableofcontents import TableOfContents
from pygments import lex
from pygments.lexers import CSharpLexer, PostgresLexer, XmlLexer, TextLexer
from pygments.token import Token

FONT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fonts") + os.sep
pdfmetrics.registerFont(TTFont("Sans", FONT_DIR + "DejaVuSans.ttf"))
pdfmetrics.registerFont(TTFont("Sans-Bold", FONT_DIR + "DejaVuSans-Bold.ttf"))
pdfmetrics.registerFont(TTFont("Sans-Oblique", FONT_DIR + "DejaVuSans-Oblique.ttf"))
pdfmetrics.registerFont(TTFont("Sans-BoldOblique", FONT_DIR + "DejaVuSans-BoldOblique.ttf"))
pdfmetrics.registerFont(TTFont("Mono", FONT_DIR + "DejaVuSansMono.ttf"))
pdfmetrics.registerFont(TTFont("Mono-Bold", FONT_DIR + "DejaVuSansMono-Bold.ttf"))
pdfmetrics.registerFont(TTFont("Mono-Oblique", FONT_DIR + "DejaVuSansMono-Oblique.ttf"))
for b, i, name in [(0, 0, "Sans"), (1, 0, "Sans-Bold"), (0, 1, "Sans-Oblique"),
                   (1, 1, "Sans-BoldOblique")]:
    addMapping("Sans", b, i, name)
for b, i, name in [(0, 0, "Mono"), (1, 0, "Mono-Bold"), (0, 1, "Mono-Oblique"),
                   (1, 1, "Mono-Bold")]:
    addMapping("Mono", b, i, name)

# ---------------------------------------------------------------- Colors & dimensions
INK = colors.HexColor("#1f2328")
MUTED = colors.HexColor("#5b6470")
ACCENT = colors.HexColor("#1d4e89")
ACCENT_LIGHT = colors.HexColor("#e8f0fa")
CODE_BG = colors.HexColor("#f6f8fa")
CODE_BORDER = colors.HexColor("#8aa9cf")
RULE = colors.HexColor("#d0d7de")
INLINE_CODE = "#8a1538"

PAGE_W, PAGE_H = A4
MARGIN_X = 24 * mm
MARGIN_TOP = 24 * mm
MARGIN_BOTTOM = 22 * mm
CONTENT_W = PAGE_W - 2 * MARGIN_X
MAX_CODE_COLS = 94

# ---------------------------------------------------------------- Styles
BODY = ParagraphStyle("Body", fontName="Sans", fontSize=9.8, leading=15, textColor=INK,
                      alignment=TA_LEFT, spaceAfter=7)
BODY_TIGHT = ParagraphStyle("BodyTight", parent=BODY, spaceAfter=3)
H1 = ParagraphStyle("H1", fontName="Sans-Bold", fontSize=15, leading=20, textColor=ACCENT,
                    spaceBefore=16, spaceAfter=8)
H2 = ParagraphStyle("H2", fontName="Sans-Bold", fontSize=11.5, leading=16, textColor=INK,
                    spaceBefore=10, spaceAfter=5)
TITLE = ParagraphStyle("Title", fontName="Sans-Bold", fontSize=22, leading=28, textColor=INK,
                       spaceAfter=6)
OVERLINE = ParagraphStyle("Overline", fontName="Sans-Bold", fontSize=8.5, leading=12,
                          textColor=ACCENT, spaceAfter=6)
SUBTITLE = ParagraphStyle("Subtitle", fontName="Sans", fontSize=11.5, leading=17,
                          textColor=MUTED, spaceAfter=14)
CODE = ParagraphStyle("Code", fontName="Mono", fontSize=7.7, leading=10.9, textColor=INK)
CODE_LABEL = ParagraphStyle("CodeLabel", fontName="Mono", fontSize=7.5, leading=10,
                            textColor=MUTED, spaceBefore=4, spaceAfter=2)
CELL = ParagraphStyle("Cell", fontName="Sans", fontSize=8.8, leading=12.5, textColor=INK)
CELL_HEAD = ParagraphStyle("CellHead", parent=CELL, fontName="Sans-Bold")
CALLOUT_BODY = ParagraphStyle("CalloutBody", parent=BODY, fontSize=9.4, leading=14,
                              spaceAfter=0)
TOC_0 = ParagraphStyle("TOC0", fontName="Sans-Bold", fontSize=10.5, leading=16, textColor=ACCENT,
                       leftIndent=0, spaceBefore=7)
TOC_1 = ParagraphStyle("TOC1", fontName="Sans", fontSize=9.6, leading=14.5, textColor=INK,
                       leftIndent=14)
PART_OVER = ParagraphStyle("PartOver", fontName="Sans-Bold", fontSize=10, leading=14,
                           textColor=ACCENT, spaceAfter=6)
PART = ParagraphStyle("Part", fontName="Sans-Bold", fontSize=26, leading=32, textColor=INK,
                      spaceAfter=10)
PART_SUB = ParagraphStyle("PartSub", fontName="Sans", fontSize=11.5, leading=18,
                          textColor=MUTED, spaceAfter=10)

# ---------------------------------------------------------------- Inline-Markup
def c(text):
    """Inline code within running text."""
    esc = text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
    return f'<font face="Mono" size="8.8" color="{INLINE_CODE}">{esc}</font>'


def p(text, style=BODY):
    return Paragraph(text, style)


# ---------------------------------------------------------------- Syntax highlighting
TOKEN_COLORS = [
    (Token.Comment, "#6a737d", True),
    (Token.Literal.String, "#a31515", False),
    (Token.Literal.Number, "#098658", False),
    (Token.Keyword.Type, "#0b6e99", False),
    (Token.Keyword, "#0000c8", False),
    (Token.Name.Class, "#267f99", False),
    (Token.Name.Builtin, "#0b6e99", False),
    (Token.Name.Attribute, "#7a3e9d", False),
    (Token.Name.Tag, "#800000", False),
    (Token.Name.Function, "#795e26", False),
    (Token.Operator.Word, "#0000c8", False),
]

LEXERS = {"csharp": CSharpLexer, "sql": PostgresLexer, "xml": XmlLexer, "text": TextLexer}


def _style_for(ttype):
    for base, color, italic in TOKEN_COLORS:
        if ttype in base:
            return color, italic
    return None, False


def _escape_code(s):
    return (s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
             .replace(" ", "&nbsp;"))


def highlight_lines(code, lang):
    lexer = LEXERS[lang](stripnl=False, ensurenl=False)
    lines, current = [], []
    for ttype, value in lex(code, lexer):
        parts = value.split("\n")
        for idx, part in enumerate(parts):
            if idx > 0:
                lines.append("".join(current))
                current = []
            if part:
                color, italic = _style_for(ttype)
                text = _escape_code(part)
                if italic:
                    text = f'<font face="Mono-Oblique">{text}</font>'
                if color:
                    text = f'<font color="{color}">{text}</font>'
                current.append(text)
    lines.append("".join(current))
    return lines


def code_block(code, lang="csharp", label=None, lead=None):
    code = code.strip("\n")
    for line in code.split("\n"):
        if len(line) > MAX_CODE_COLS:
            raise ValueError(f"Code line too long ({len(line)}): {line}")
    rows = [[Paragraph(m if m else "&nbsp;", CODE)] for m in highlight_lines(code, lang)]
    n = len(rows)
    table = Table(rows, colWidths=[CONTENT_W], splitByRow=1)
    style = [
        ("BACKGROUND", (0, 0), (-1, -1), CODE_BG),
        ("LINEBEFORE", (0, 0), (0, -1), 2.5, CODE_BORDER),
        ("LEFTPADDING", (0, 0), (-1, -1), 11),
        ("RIGHTPADDING", (0, 0), (-1, -1), 8),
        ("TOPPADDING", (0, 0), (-1, -1), 0),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 0),
        ("TOPPADDING", (0, 0), (-1, 0), 7),
        ("BOTTOMPADDING", (0, n - 1), (-1, n - 1), 7),
    ]
    table.setStyle(TableStyle(style))
    parts = []
    if label:
        parts.append(Paragraph(_escape_code(label), CODE_LABEL))
    parts.append(table)
    parts.append(Spacer(1, 9))
    height = n * CODE.leading + 14 + (14 if label else 0) + 9
    lead = lead or []
    for f in lead:  # keep heading/introduction together with the code
        _, fh = f.wrap(CONTENT_W, PAGE_H)
        st = getattr(f, "style", None)
        height += fh + (st.spaceBefore + st.spaceAfter if st else 0)
    frame_h = PAGE_H - MARGIN_TOP - MARGIN_BOTTOM
    if height < frame_h * 0.9:
        # Fits on one page: never split it, move it to the next page as a whole if needed
        return [CondPageBreak(height)] + lead + parts
    return [CondPageBreak(70 * mm)] + lead + parts


# ---------------------------------------------------------------- Callout boxes
CALLOUTS = {
    "wichtig": ("important", colors.HexColor("#fff7e6"), colors.HexColor("#d99a00")),
    "tipp": ("tip", colors.HexColor("#eaf5ee"), colors.HexColor("#2e8b57")),
    "hinweis": ("note", ACCENT_LIGHT, ACCENT),
}
CALLOUTS["important"] = CALLOUTS["wichtig"]
CALLOUTS["tip"] = CALLOUTS["tipp"]
CALLOUTS["note"] = CALLOUTS["hinweis"]


def callout(kind, text, title=None):
    title_key, bg, border = CALLOUTS[kind]
    default_title = T(title_key)
    head = ParagraphStyle("CalloutHead", parent=CALLOUT_BODY, fontName="Sans-Bold",
                          textColor=border, spaceAfter=3)
    content = [Paragraph(title or default_title, head)]
    for para in ([text] if isinstance(text, str) else text):
        content.append(Paragraph(para, CALLOUT_BODY))
        content.append(Spacer(1, 4))
    content.pop()
    t = Table([[content]], colWidths=[CONTENT_W])
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), bg),
        ("LINEBEFORE", (0, 0), (0, -1), 3, border),
        ("LEFTPADDING", (0, 0), (-1, -1), 12),
        ("RIGHTPADDING", (0, 0), (-1, -1), 10),
        ("TOPPADDING", (0, 0), (-1, -1), 8),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 9),
    ]))
    return [KeepTogether([t, Spacer(1, 10)])]


# ---------------------------------------------------------------- Tables
_MONO_RUN = re.compile(r'<font face="Mono"[^>]*>(.*?)</font>', re.S)
_TAG = re.compile(r"<[^>]+>")


def _unescape(text):
    return text.replace("&lt;", "<").replace("&gt;", ">").replace("&amp;", "&")


def _min_cell_width(cell, style):
    """Width of the longest unbreakable run in a cell (inline code counts in Mono)."""
    widest = 0.0
    for run in _MONO_RUN.findall(cell):
        for token in _unescape(run).split():
            widest = max(widest, pdfmetrics.stringWidth(token, "Mono", 8.8))
    prose = _unescape(_TAG.sub(" ", _MONO_RUN.sub(" ", cell)))
    for token in prose.split():
        widest = max(widest, pdfmetrics.stringWidth(token, "Sans-Bold", style.fontSize))
    return widest


def _fit_widths(col_w, minimums):
    """Widens columns whose longest identifier would otherwise be split mid-word.

    The extra space is taken proportionally from columns that have room to spare; if the
    table is simply too narrow, ReportLab still splits as before.
    """
    col_w = list(col_w)
    for _ in range(len(col_w)):
        short = [i for i, w in enumerate(col_w) if w < minimums[i]]
        if not short:
            break
        need = sum(minimums[i] - col_w[i] for i in short)
        spare = {i: col_w[i] - minimums[i] for i in range(len(col_w))
                 if i not in short and col_w[i] > minimums[i]}
        available = sum(spare.values())
        if available <= 0:
            break
        take = min(need, available)
        for i in short:
            col_w[i] += (minimums[i] - col_w[i]) * take / need
        for i, room in spare.items():
            col_w[i] -= room * take / available
    return col_w


def data_table(header, rows, widths):
    total = sum(widths)
    col_w = [CONTENT_W * w / total for w in widths]
    padding = 12          # LEFTPADDING + RIGHTPADDING below
    minimums = [
        max([_min_cell_width(header[i], CELL_HEAD)]
            + [_min_cell_width(row[i], CELL) for row in rows if i < len(row)]) + padding + 1
        for i in range(len(col_w))
    ]
    col_w = _fit_widths(col_w, minimums)
    data = [[Paragraph(h, CELL_HEAD) for h in header]]
    data += [[Paragraph(cell, CELL) for cell in row] for row in rows]
    t = Table(data, colWidths=col_w, repeatRows=1)
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), ACCENT_LIGHT),
        ("LINEBELOW", (0, 0), (-1, 0), 0.8, ACCENT),
        ("LINEBELOW", (0, 1), (-1, -1), 0.4, RULE),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 6),
        ("RIGHTPADDING", (0, 0), (-1, -1), 6),
        ("TOPPADDING", (0, 0), (-1, -1), 5),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
    ]))
    return [t, Spacer(1, 12)]


class FlowDiagram(Flowable):
    """Vertical pipeline: rounded boxes with the stage name, connected by vector arrows,
    and the description of each stage to the right of its box."""

    BOX_W = 0.40          # share of the content width taken by the boxes
    GAP = 14              # space between box and description
    ARROW = 16            # vertical space for the arrow between two boxes
    PAD_X, PAD_Y = 8, 6

    def __init__(self, steps):
        super().__init__()
        self.box_style = ParagraphStyle("FlowBox", parent=CELL, fontName="Sans-Bold",
                                        alignment=1, textColor=ACCENT)
        self.desc_style = ParagraphStyle("FlowDesc", parent=CELL, textColor=INK)
        self.steps = [(Paragraph(title, self.box_style), Paragraph(desc, self.desc_style))
                      for title, desc in steps]

    def wrap(self, avail_width, avail_height):
        self.width = avail_width
        # Wide enough for the longest unbreakable stage name, but leave room for the text
        widest = max(pdfmetrics.stringWidth(word, "Sans-Bold", self.box_style.fontSize)
                     for box, _ in self.steps for word in _TAG.sub(" ", box.text).split())
        self.box_w = min(max(avail_width * self.BOX_W, widest + 2 * self.PAD_X + 2),
                         avail_width * 0.55)
        self.desc_w = avail_width - self.box_w - self.GAP
        self.rows = []
        for box, desc in self.steps:
            _, bh = box.wrap(self.box_w - 2 * self.PAD_X, avail_height)
            _, dh = desc.wrap(self.desc_w, avail_height)
            box_h = bh + 2 * self.PAD_Y
            self.rows.append((box_h, max(box_h, dh), bh, dh))
        self.height = sum(r[1] for r in self.rows) + self.ARROW * (len(self.rows) - 1)
        return self.width, self.height

    def draw(self):
        canv = self.canv
        y = self.height
        for i, ((box, desc), (box_h, row_h, bh, dh)) in enumerate(zip(self.steps, self.rows)):
            top = y
            box_y = top - (row_h - box_h) / 2 - box_h
            canv.setFillColor(ACCENT_LIGHT)
            canv.setStrokeColor(CODE_BORDER)
            canv.setLineWidth(0.8)
            canv.roundRect(0, box_y, self.box_w, box_h, 4, stroke=1, fill=1)
            box.drawOn(canv, self.PAD_X, box_y + self.PAD_Y)

            # thin connector from the box to its description
            mid = box_y + box_h / 2
            canv.setStrokeColor(RULE)
            canv.setLineWidth(0.6)
            canv.line(self.box_w, mid, self.box_w + self.GAP - 4, mid)
            desc.drawOn(canv, self.box_w + self.GAP, mid - dh / 2)

            y = top - row_h
            if i < len(self.steps) - 1:
                x = self.box_w / 2
                canv.setStrokeColor(ACCENT)
                canv.setFillColor(ACCENT)
                canv.setLineWidth(1.1)
                canv.line(x, box_y, x, y - self.ARROW + 5)
                tip = y - self.ARROW + 1
                path = canv.beginPath()
                path.moveTo(x - 3.5, tip + 5)
                path.lineTo(x + 3.5, tip + 5)
                path.lineTo(x, tip)
                path.close()
                canv.drawPath(path, stroke=0, fill=1)
                y -= self.ARROW


def flow_diagram(steps):
    """Vertical flow of boxes connected by arrows (for pipelines)."""
    return [Spacer(1, 2), FlowDiagram(steps), Spacer(1, 14)]


class Rule(Flowable):
    def __init__(self, width=CONTENT_W, color=RULE, thickness=0.8, space=6):
        super().__init__()
        self.width, self.color, self.thickness, self.space = width, color, thickness, space

    def wrap(self, *_):
        return self.width, self.space * 2

    def draw(self):
        self.canv.setStrokeColor(self.color)
        self.canv.setLineWidth(self.thickness)
        self.canv.line(0, self.space, self.width, self.space)


# ---------------------------------------------------------------- Headings + TOC
class Heading(Paragraph):
    def __init__(self, text, style, level, key):
        super().__init__(text, style)
        self.toc_level, self.toc_key, self.toc_text = level, key, text


_heading_counter = [0]


_part_counter = [0]
ROMAN = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X"]


def h1(text, numbered=True):
    _heading_counter[0] += 1
    num = _heading_counter[0]
    label = f"{num}&nbsp;&nbsp;{text}" if numbered else text
    level = 1 if _part_counter[0] else 0
    return [CondPageBreak(75 * mm), Heading(label, H1, level, f"h{num}")]


def part(title, subtitle, contents=None):
    """New part of the book: own page with title, subtitle and chapter overview."""
    _part_counter[0] += 1
    n = _part_counter[0]
    items = [PageBreak(), Spacer(1, 50 * mm),
             Paragraph(f"{T('part')} {ROMAN[n - 1]}", PART_OVER),
             Heading(title, PART, 0, f"p{n}"),
             Rule(color=ACCENT, thickness=1.6, space=6),
             Paragraph(subtitle, PART_SUB)]
    if contents:
        items.append(Spacer(1, 6))
        for line in contents:
            items.append(Paragraph("›&nbsp;&nbsp;" + line, BODY_TIGHT))
    items.append(PageBreak())
    return items


def h2(text):
    return [CondPageBreak(45 * mm), Paragraph(text, H2)]


def toc():
    t = TableOfContents()
    t.levelStyles = [TOC_0, TOC_1]
    t.dotsMinLevel = 0
    return t


# ---------------------------------------------------------------- Document
class NumberedCanvas(rl_canvas.Canvas):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self._saved = []

    def showPage(self):
        self._saved.append(dict(self.__dict__))
        self._startPage()

    def save(self):
        total = len(self._saved)
        for state in self._saved:
            self.__dict__.update(state)
            self._draw_footer(total)
            super().showPage()
        super().save()

    def _draw_footer(self, total):
        self.setFont("Sans", 8)
        self.setFillColor(MUTED)
        self.drawCentredString(PAGE_W / 2, 12 * mm, T("page").format(page=self._pageNumber, total=total))


class Doc(BaseDocTemplate):
    def __init__(self, filename, header_left, header_right, title, subject):
        super().__init__(filename, pagesize=A4, leftMargin=MARGIN_X, rightMargin=MARGIN_X,
                         topMargin=MARGIN_TOP, bottomMargin=MARGIN_BOTTOM, title=title,
                         author="guenni81", subject=subject)
        self.header_left, self.header_right = header_left, header_right
        frame = Frame(MARGIN_X, MARGIN_BOTTOM, CONTENT_W, PAGE_H - MARGIN_TOP - MARGIN_BOTTOM,
                      leftPadding=0, rightPadding=0, topPadding=0, bottomPadding=0)
        self.addPageTemplates([PageTemplate("main", [frame], onPage=self._header)])

    def _header(self, canv, doc):
        if doc.page == 1:
            return
        canv.saveState()
        canv.setFont("Sans", 7.8)
        canv.setFillColor(MUTED)
        y = PAGE_H - 14 * mm
        canv.drawString(MARGIN_X, y, self.header_left)
        canv.drawRightString(PAGE_W - MARGIN_X, y, self.header_right)
        canv.setStrokeColor(RULE)
        canv.setLineWidth(0.6)
        canv.line(MARGIN_X, y - 3, PAGE_W - MARGIN_X, y - 3)
        canv.restoreState()

    def afterFlowable(self, flowable):
        if isinstance(flowable, Heading):
            key = flowable.toc_key
            self.canv.bookmarkPage(key)
            plain = flowable.toc_text.replace("&nbsp;", " ")
            self.canv.addOutlineEntry(plain, key, level=flowable.toc_level)
            self.notify("TOCEntry", (flowable.toc_level, flowable.toc_text, self.page, key))


def build(path, story, header_left, header_right, title, subject):
    doc = Doc(path, header_left, header_right, title, subject)
    doc.multiBuild(story, canvasmaker=NumberedCanvas)


def title_block(overline, title, subtitle, meta_rows):
    items = [Paragraph(overline.upper(), OVERLINE), Paragraph(title, TITLE),
             Paragraph(subtitle, SUBTITLE), Rule(space=4)]
    label = ParagraphStyle("MetaLabel", parent=CELL, fontName="Sans-Bold", textColor=MUTED)
    data = [[Paragraph(k, label), Paragraph(v, CELL)] for k, v in meta_rows]
    t = Table(data, colWidths=[CONTENT_W * 0.2, CONTENT_W * 0.8])
    t.setStyle(TableStyle([("VALIGN", (0, 0), (-1, -1), "TOP"),
                           ("LEFTPADDING", (0, 0), (-1, -1), 0),
                           ("TOPPADDING", (0, 0), (-1, -1), 3),
                           ("BOTTOMPADDING", (0, 0), (-1, -1), 4)]))
    items += [Spacer(1, 4), t, Spacer(1, 6), Rule(space=4), Spacer(1, 8)]
    return items
