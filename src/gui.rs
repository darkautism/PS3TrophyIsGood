//! Pure Rust GPUI frontend. No WebView, JS, Tauri, or advertisements.
use gpui::{
    App, Bounds, Context, Render, Window, WindowBounds, WindowOptions,
    div, prelude::*, px, rgb, size, uniform_list,
};
use gpui_platform::application;
use ps3trophyisgood::{SortKey, TrophySession, format_timestamp};
use std::collections::HashMap;
use std::sync::Arc;

const BG: u32 = 0x0f1117;
const SURFACE: u32 = 0x1a1e29;
const HOVER: u32 = 0x252d3c;
const MUTED: u32 = 0x9da7ba;
const TEXT: u32 = 0xecf0fa;
const ACCENT: u32 = 0x63a7ff;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Filter {
    All,
    Obtained,
    Locked,
}

pub struct TrophyApp {
    session: Option<TrophySession>,
    icons: HashMap<u32, Arc<gpui::Image>>,
    visible: Vec<usize>,
    selected_id: Option<u32>,
    sort: SortKey,
    descending: bool,
    filter: Filter,
    status: String,
}

impl TrophyApp {
    fn new() -> Self {
        let mut app = Self {
            session: None,
            icons: HashMap::new(),
            visible: Vec::new(),
            selected_id: None,
            sort: SortKey::Id,
            descending: false,
            filter: Filter::All,
            status: "Open a PS3 or RPCS3 trophy folder to begin.".into(),
        };
        if let Some(folder) = std::env::args_os().nth(1) {
            app.open_folder(std::path::PathBuf::from(folder));
        }
        app
    }

    fn open_folder(&mut self, path: std::path::PathBuf) {
        match TrophySession::open(&path) {
            Ok(session) => {
                let total = session.set.trophies.len();
                let format = session.set.format.label();
                self.selected_id = None;
                self.icons.clear();
                // Cache each PNG once, keyed by the real trophy ID rather than
                // the row position (which changes when sorting or filtering).
                for trophy in &session.set.trophies {
                    let icon_path = path.join(format!("TROP{:03}.PNG", trophy.id));
                    if let Ok(bytes) = std::fs::read(&icon_path) {
                        self.icons.insert(trophy.id, Arc::new(
                            gpui::Image::from_bytes(gpui::ImageFormat::Png, bytes)));
                    }
                }
                self.status = format!("{format} | {total} trophies | {} | {}",
                    session.set.npcommid, path.display());
                self.session = Some(session);
                self.rebuild();
            }
            Err(e) => {
                self.status = format!("Could not open {}: {e:#}", path.display());
            }
        }
    }

    fn rebuild(&mut self) {
        let Some(session) = self.session.as_ref() else { return };
        self.visible = (0..session.set.trophies.len())
            .filter(|&ix| {
                let trophy = &session.set.trophies[ix];
                match self.filter {
                    Filter::All => true,
                    Filter::Obtained => trophy.obtained,
                    Filter::Locked => !trophy.obtained,
                }
            }).collect();

        self.visible.sort_by(|&a, &b| {
            let first = &session.set.trophies[a];
            let second = &session.set.trophies[b];
            let ordering = match self.sort {
                SortKey::Id => first.id.cmp(&second.id),
                SortKey::Name => first.name.to_lowercase().cmp(&second.name.to_lowercase()),
                SortKey::Grade => grade_rank(first.grade).cmp(&grade_rank(second.grade)),
                SortKey::EarnedTime => first.earned_us.cmp(&second.earned_us),
            };
            let ordering = if self.descending { ordering.reverse() } else { ordering };
            ordering.then_with(|| first.id.cmp(&second.id))
        });
    }

    fn set_sort(&mut self, key: SortKey, cx: &mut Context<Self>) {
        if self.sort == key {
            self.descending = !self.descending;
        } else {
            self.sort = key;
            self.descending = false;
        }
        self.rebuild();
        cx.notify();
    }

    fn set_filter(&mut self, filter: Filter, cx: &mut Context<Self>) {
        self.filter = filter;
        self.rebuild();
        cx.notify();
    }

    fn unlock_selected(&mut self, cx: &mut Context<Self>) {
        let Some(id) = self.selected_id else {
            self.status = "Select a trophy first.".into();
            cx.notify();
            return;
        };
        if let Some(session) = self.session.as_mut() {
            let at = chrono::Utc::now().timestamp_micros();
            match session.unlock_at_utc(id, at) {
                Ok(()) => {
                    self.status = format!("Changed trophy #{id} in memory; click Save RPCS3 to persist.");
                    self.rebuild();
                }
                Err(e) => self.status = format!("Cannot unlock trophy #{id}: {e:#}"),
            }
        }
        cx.notify();
    }

    fn lock_selected(&mut self, cx: &mut Context<Self>) {
        let Some(id) = self.selected_id else {
            self.status = "Select a trophy first.".into();
            cx.notify();
            return;
        };
        if let Some(session) = self.session.as_mut() {
            match session.lock(id) {
                Ok(()) => {
                    self.status = format!("Locked trophy #{id} in memory; click Save RPCS3 to persist.");
                    self.rebuild();
                }
                Err(e) => self.status = format!("Cannot lock trophy #{id}: {e:#}"),
            }
        }
        cx.notify();
    }

    fn save(&mut self, cx: &mut Context<Self>) {
        let result = self.session.as_ref()
            .map(|session| (session.source.clone(), session.save_rpcs3_with_backup()));
        match result {
            Some((path, Ok(backup))) => {
                self.open_folder(path);
                self.status = format!("Saved safely. Original DAT backup: {}", backup.display());
            }
            Some((_, Err(e))) => self.status = format!("Save refused: {e:#}"),
            None => self.status = "Open an RPCS3 trophy folder first.".into(),
        }
        cx.notify();
    }
}

fn grade_rank(grade: ps3trophyisgood::Grade) -> u8 {
    use ps3trophyisgood::Grade::*;
    match grade {
        Platinum => 0,
        Gold => 1,
        Silver => 2,
        Bronze => 3,
    }
}

fn toolbar_button(label: &'static str, active: bool) -> gpui::Stateful<gpui::Div> {
    div().id(label)
        .px_3()
        .py_2()
        .rounded_md()
        .bg(rgb(if active { 0x2a4263 } else { SURFACE }))
        .text_sm()
        .text_color(rgb(if active { 0xd9e8ff } else { TEXT }))
        .cursor_pointer()
        .child(label)
}

impl Render for TrophyApp {
    fn render(&mut self, _window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let loaded = self.session.is_some();
        let title = self.session.as_ref()
            .map(|s| s.set.title.clone())
            .unwrap_or_else(|| "PS3TrophyIsGood".to_string());
        let count = self.visible.len();
        let total = self.session.as_ref().map_or(0, |s| s.set.trophies.len());
        let obtained = self.session.as_ref().map_or(0,
            |s| s.set.trophies.iter().filter(|t| t.obtained).count());
        let selected = self.selected_id.and_then(|id|
            self.session.as_ref()
                .and_then(|s| s.set.trophies.iter().find(|t| t.id == id)))
            .cloned();
        let open_button = div().id("open-folder")
            .px_4().py_2()
            .rounded_md().bg(rgb(ACCENT))
            .text_color(rgb(BG))
            .cursor_pointer()
            .child("Open Trophy Folder")
            .on_click(cx.listener(|this, _event, _window, cx| {
                if let Some(path) = rfd::FileDialog::new()
                    .set_title("Select PS3 / RPCS3 trophy directory")
                    .pick_folder()
                {
                    this.open_folder(path);
                    cx.notify();
                }
            }));

        let sort_name = self.sort == SortKey::Name;
        let sort_grade = self.sort == SortKey::Grade;
        let sort_date = self.sort == SortKey::EarnedTime;
        let header = div()
            .w_full()
            .flex()
            .items_center()
            .gap_2()
            .h(px(38.))
            .px_3()
            .bg(rgb(SURFACE))
            .text_sm()
            .text_color(rgb(MUTED))
            .child(div().w(px(40.)).child(""))
            .child(div().w(px(55.)).child("ID"))
            .child(div().id("sort-name").w(px(280.)).cursor_pointer().text_color(rgb(if sort_name { ACCENT } else { MUTED }))
                .child("Trophy Name")
                .on_click(cx.listener(|this, _, _, cx| this.set_sort(SortKey::Name, cx))))
            .child(div().id("sort-grade").w(px(105.)).cursor_pointer().text_color(rgb(if sort_grade { ACCENT } else { MUTED }))
                .child("Type")
                .on_click(cx.listener(|this, _, _, cx| this.set_sort(SortKey::Grade, cx))))
            .child(div().w(px(72.)).child("Earned"))
            .child(div().w(px(72.)).child("Synced"))
            .child(div().id("sort-date").w(px(205.)).cursor_pointer().text_color(rgb(if sort_date { ACCENT } else { MUTED }))
                .child("Obtainment Time")
                .on_click(cx.listener(|this, _, _, cx| this.set_sort(SortKey::EarnedTime, cx))))
            .child(div().w(px(100.)).child("From"));

        div()
            .size_full()
            .flex()
            .flex_col()
            .bg(rgb(BG))
            .text_color(rgb(TEXT))
            .child(
                div()
                    .w_full()
                    .px_5()
                    .py_4()
                    .flex()
                    .items_center()
                    .justify_between()
                    .bg(rgb(SURFACE))
                    .child(div().flex().flex_col()
                        .child(div().text_xl().child(title))
                        .child(div().text_sm().text_color(rgb(MUTED))
                            .child(format!("{obtained} / {total} unlocked"))))
                    .child(open_button)
            )
            .child(
                div()
                    .w_full()
                    .px_4().py_3()
                    .flex().items_center().gap_2()
                    .child(toolbar_button("All", self.filter == Filter::All)
                        .on_click(cx.listener(|this, _, _, cx| this.set_filter(Filter::All, cx))))
                    .child(toolbar_button("Obtained", self.filter == Filter::Obtained)
                        .on_click(cx.listener(|this, _, _, cx| this.set_filter(Filter::Obtained, cx))))
                    .child(toolbar_button("Locked", self.filter == Filter::Locked)
                        .on_click(cx.listener(|this, _, _, cx| this.set_filter(Filter::Locked, cx))))
                    .child(div().px_3().text_sm().text_color(rgb(MUTED))
                        .child(format!("{count} shown  •  Click columns to sort")))
                    .child(toolbar_button("Unlock Now", false)
                        .on_click(cx.listener(|this, _, _, cx| this.unlock_selected(cx))))
                    .child(toolbar_button("Lock", false)
                        .on_click(cx.listener(|this, _, _, cx| this.lock_selected(cx))))
                    .child(toolbar_button("Save RPCS3", false)
                        .on_click(cx.listener(|this, _, _, cx| this.save(cx))))
            )
            .child(
                div()
                    .flex_1()
                    .min_h_0()
                    .flex()
                    .flex_col()
                    .px_4()
                    .child(header)
                    .child(
                        div().flex_1().min_h_0()
                            .overflow_hidden()
                            .border_1().border_color(rgb(0x30394a))
                            .child(
                                uniform_list("trophy-rows", count, cx.processor(|this, range: std::ops::Range<usize>, _window, cx| {
                                    let mut elements = Vec::with_capacity(range.len());
                                    for index in range {
                                        let Some(&ix) = this.visible.get(index) else { continue };
                                        let Some(trophy) = this.session.as_ref()
                                            .and_then(|s| s.set.trophies.get(ix)).cloned() else { continue };
                                        let id = trophy.id;
                                        let selected = this.selected_id == Some(id);
                                        let icon = this.icons.get(&id).cloned();
                                        let row = div().id(("trophy", id))
                                            .h(px(43.)).w_full()
                                            .px_3().flex().items_center().gap_2()
                                            .bg(rgb(if selected { 0x29466a } else if index % 2 == 0 { BG } else { 0x151923 }))
                                            .border_b_1().border_color(rgb(0x242a34))
                                            .text_sm()
                                            .cursor_pointer()
                                            .child(div().w(px(40.)).h(px(36.))
                                                .when_some(icon, |d, image|
                                                    d.child(gpui::img(image).w(px(32.)).h(px(32.)))))
                                            .child(div().w(px(55.)).text_color(rgb(MUTED)).child(format!("#{id:03}")))
                                            .child(div().w(px(280.)).overflow_hidden().child(trophy.name))
                                            .child(div().w(px(105.)).child(trophy.grade.label()))
                                            .child(div().w(px(72.)).child(if trophy.obtained { "Yes" } else { "No" }))
                                            .child(div().w(px(72.)).child(if trophy.synced { "Yes" } else { "No" }))
                                            .child(div().w(px(205.)).text_color(rgb(if trophy.obtained { TEXT } else { MUTED }))
                                                .child(format_timestamp(trophy.earned_us)))
                                            .child(div().w(px(100.)).text_color(rgb(MUTED))
                                                .child(if trophy.group_id == 0 { "Base".into() } else { format!("DLC {}", trophy.group_id) }))
                                            .on_click(cx.listener(move |this, _, _, cx| {
                                                this.selected_id = Some(id);
                                                cx.notify();
                                            }));
                                        elements.push(row);
                                    }
                                    elements
                                }))
                                .h_full()
                            )
                    )
            )
            .when_some(selected, |root, trophy| {
                root.child(
                    div()
                        .px_5().py_3()
                        .w_full()
                        .bg(rgb(HOVER))
                        .border_t_1().border_color(rgb(0x39475b))
                        .flex().flex_col().gap_1()
                        .child(div().text_sm().child(format!("#{}  {}  ·  {}",
                            trophy.id, trophy.name, trophy.grade.label())))
                        .child(div().text_sm().text_color(rgb(MUTED)).child(trophy.description))
                )
            })
            .child(
                div()
                    .w_full().px_5().py_2()
                    .bg(rgb(SURFACE))
                    .text_sm().text_color(rgb(MUTED))
                    .child(if loaded {
                        self.status.clone()
                    } else {
                        self.status.clone()
                    })
            )
    }
}

pub fn run() {
    application().run(|cx: &mut App| {
        let bounds = Bounds::centered(None, size(px(1120.), px(740.)), cx);
        if let Err(error) = cx.open_window(
            WindowOptions {
                window_bounds: Some(WindowBounds::Windowed(bounds)),
                ..Default::default()
            },
            |_, cx| cx.new(|_| TrophyApp::new()),
        ) {
            eprintln!("Could not create GPU window: {error}");
        }
        cx.activate(true);
    });
}
