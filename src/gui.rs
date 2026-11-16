//! Pure Rust GPUI frontend. No WebView, JS, Tauri, or advertisements.
use gpui::{
    App, Bounds, Context, Render, Window, WindowBounds, WindowOptions,
    div, prelude::*, px, rgb, size, uniform_list,
};
use gpui_platform::application;
use gpui_ce_elements::editable_text::{
    EditableTextState, StringStorage, text_input,
    actions::{default_bindings, DEFAULT_INPUT_CONTEXT},
};
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
    dirty: bool,
    time_input: gpui::Entity<EditableTextState>,
    random_start_input: gpui::Entity<EditableTextState>,
    random_end_input: gpui::Entity<EditableTextState>,
}

impl TrophyApp {
    fn new(cx: &mut Context<Self>) -> Self {
        let now = chrono::Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Secs, true);
        let time_input = cx.new(|cx| EditableTextState::new(StringStorage::from(now.clone()), cx));
        let random_start_input = cx.new(|cx| EditableTextState::new(
            StringStorage::from("2008-01-01T00:00:00Z"), cx));
        let random_end_input = cx.new(|cx| EditableTextState::new(StringStorage::from(now), cx));
        let mut app = Self {
            session: None,
            icons: HashMap::new(),
            visible: Vec::new(),
            selected_id: None,
            sort: SortKey::Id,
            descending: false,
            filter: Filter::All,
            status: "Drag a PS3 trophy folder here, or click Open Folder.".into(),
            dirty: false,
            time_input,
            random_start_input,
            random_end_input,
        };
        if let Some(folder) = std::env::args_os().nth(1) {
            app.open_folder(std::path::PathBuf::from(folder));
        }
        app
    }

    fn confirm_unsaved(&mut self) -> bool {
        if !self.dirty {
            return true;
        }
        use rfd::{MessageButtons, MessageDialog, MessageDialogResult};
        match MessageDialog::new()
            .set_title("Unsaved trophy edits")
            .set_description("Save changes to the currently opened trophy set before closing?")
            .set_buttons(MessageButtons::YesNoCancel)
            .show()
        {
            MessageDialogResult::Yes => {
                if let Some(session) = self.session.as_ref() {
                    if let Err(error) = session.save_with_backup() {
                        self.status = format!("Unable to save current trophy set: {error:#}");
                        return false;
                    }
                }
                self.dirty = false;
                true
            }
            MessageDialogResult::No => true,
            _ => false,
        }
    }

    fn open_folder(&mut self, path: std::path::PathBuf) {
        if !self.confirm_unsaved() {
            return;
        }
        match TrophySession::open(&path) {
            Ok(session) => {
                let total = session.set.trophies.len();
                let format = session.set.format.label();
                self.selected_id = None;
                self.dirty = false;
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

    fn refresh_folder(&mut self, cx: &mut Context<Self>) {
        if let Some(path) = self.session.as_ref().map(|s| s.source.clone()) {
            self.open_folder(path);
        }
        cx.notify();
    }

    fn close_folder(&mut self, cx: &mut Context<Self>) {
        if self.confirm_unsaved() {
            self.session = None;
            self.icons.clear();
            self.visible.clear();
            self.selected_id = None;
            self.dirty = false;
            self.status = "No trophy folder is open. Drag one into this window or click Open Folder.".into();
        }
        cx.notify();
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
                    self.status = format!("Changed trophy #{id} in memory; click Save to persist.");
                    self.dirty = true;
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
                    self.status = format!("Locked trophy #{id} in memory; click Save to persist.");
                    self.dirty = true;
                    self.rebuild();
                }
                Err(e) => self.status = format!("Cannot lock trophy #{id}: {e:#}"),
            }
        }
        cx.notify();
    }

    fn instant_platinum(&mut self, cx: &mut Context<Self>) {
        if let Some(session) = self.session.as_mut() {
            // The original default random interval starts on 2008-01-01 and
            // ends at the current time; native PSN sync times tighten the
            // interval in the data layer.
            let now = chrono::Utc::now().timestamp_micros();
            match session.instant_platinum(1_199_145_600_000_000, now) {
                Ok(count) => {
                    self.status = format!("{count} trophies unlocked in memory. Click Save to persist.");
                    if count > 0 { self.dirty = true; }
                    self.rebuild();
                }
                Err(error) => self.status = format!("Instant Platinum failed: {error:#}"),
            }
        }
        cx.notify();
    }

    fn clear_unsynced(&mut self, cx: &mut Context<Self>) {
        if let Some(session) = self.session.as_mut() {
            match session.clear_unsynced_tail() {
                Ok(count) => {
                    self.status = format!("{count} unsynchronized trophies cleared in memory. Click Save to persist.");
                    if count > 0 { self.dirty = true; }
                    self.rebuild();
                }
                Err(error) => self.status = format!("Clear trophies failed: {error:#}"),
            }
        }
        cx.notify();
    }

    fn select_resign_profile(&mut self, cx: &mut Context<Self>) {
        let Some(session) = self.session.as_mut() else {
            self.status = "Open a native PlayStation 3 trophy folder first.".into();
            cx.notify();
            return;
        };
        let Some(path) = rfd::FileDialog::new()
            .set_title("Select account profile SFO for PS3 re-signing")
            .add_filter("PlayStation 3 PARAM.SFO", &["sfo", "SFO"])
            .pick_file()
        else {
            return;
        };
        match session.select_profile_for_resign(&path) {
            Ok(()) => {
                self.dirty = true;
                self.status = format!("PS3 profile selected: {}. Click Save to re-sign.", path.display());
            }
            Err(error) => {
                self.status = format!("Cannot re-sign with that profile: {error:#}");
            }
        }
        cx.notify();
    }

    fn save(&mut self, cx: &mut Context<Self>) {
        let result = self.session.as_ref()
            .map(|session| (session.source.clone(), session.save_with_backup()));
        match result {
            Some((path, Ok(backup))) => {
                self.dirty = false;
                self.open_folder(path);
                self.status = format!("Saved trophy data. Originals backed up at {}", backup.display());
            }
            Some((_, Err(e))) => self.status = format!("Save refused: {e:#}"),
            None => self.status = "Open a trophy folder first.".into(),
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
        let obtained = self.session.as_ref().map_or(0, |s| s.set.obtained_count());
        let synced_count = self.session.as_ref().map_or(0, |s| s.set.synced_count());
        let (earned_xp, total_xp) = self.session.as_ref()
            .map(|s| s.set.psn_xp()).unwrap_or((0, 0));
        let xp_percent = if total_xp == 0 { 0.0 } else {
            earned_xp as f32 / total_xp as f32
        };
        let format_label = self.session.as_ref().map(|s| s.set.format.label())
            .unwrap_or("PlayStation 3");
        let selected = self.selected_id.and_then(|id|
            self.session.as_ref()
                .and_then(|s| s.set.trophies.iter().find(|t| t.id == id)))
            .cloned();
        let open_button = div().id("open-folder")
            .px_4().py_2()
            .rounded_md().bg(rgb(ACCENT))
            .text_color(rgb(BG))
            .cursor_pointer()
            .child("Open Folder")
            .on_click(cx.listener(|this, _event, _window, cx| {
                if let Some(path) = rfd::FileDialog::new()
                    .set_title("Select PS3 trophy folder")
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
            .child(div().w(px(48.)).child(""))
            .child(div().w(px(45.)).child("ID"))
            .child(div().id("sort-name").w(px(190.)).cursor_pointer().text_color(rgb(if sort_name { ACCENT } else { MUTED }))
                .child("Trophy Name")
                .on_click(cx.listener(|this, _, _, cx| this.set_sort(SortKey::Name, cx))))
            .child(div().w(px(280.)).child("Description"))
            .child(div().id("sort-grade").w(px(78.)).cursor_pointer().text_color(rgb(if sort_grade { ACCENT } else { MUTED }))
                .child("Type")
                .on_click(cx.listener(|this, _, _, cx| this.set_sort(SortKey::Grade, cx))))
            .child(div().w(px(58.)).child("Hidden"))
            .child(div().w(px(55.)).child("Get"))
            .child(div().w(px(62.)).child("IsSync"))
            .child(div().id("sort-date").w(px(190.)).cursor_pointer().text_color(rgb(if sort_date { ACCENT } else { MUTED }))
                .child("Obtainment Time")
                .on_click(cx.listener(|this, _, _, cx| this.set_sort(SortKey::EarnedTime, cx))))
            .child(div().w(px(72.)).child("From"));

        div()
            .id("trophy-app-root")
            .on_drop(cx.listener(|this, paths: &gpui::ExternalPaths, _window, cx| {
                if let Some(path) = paths.paths().iter().find(|p| {
                    p.is_dir() || p.file_name().is_some_and(|n|
                        n.to_string_lossy().eq_ignore_ascii_case("TROPCONF.SFM"))
                }) {
                    let folder = if path.is_dir() { path.clone() } else {
                        path.parent().unwrap_or(path).to_path_buf()
                    };
                    this.open_folder(folder);
                    cx.notify();
                }
            }))
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
                    .child(div().flex().flex_col().gap_1()
                        .child(div().text_xl().child(title))
                        .child(div().text_sm().text_color(rgb(MUTED))
                            .child(format!("{format_label}  ·  {obtained}/{total} obtained  ·  {synced_count}/{total} synced"))))
                    .child(div().flex().items_center().gap_4()
                        .child(div().flex().flex_col().gap_1()
                            .child(div().text_sm().text_color(rgb(ACCENT))
                                .child(format!("PSN EXP   {earned_xp} / {total_xp}")))
                            .child(div().w(px(260.)).h(px(7.)).rounded_full().bg(rgb(0x303c50))
                                .child(div().h(px(7.)).rounded_full().bg(rgb(ACCENT))
                                    .w(px(260. * xp_percent)))))
                        .child(open_button))
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
                    .child(toolbar_button("Unlock Selected", false)
                        .on_click(cx.listener(|this, _, _, cx| this.unlock_selected(cx))))
                    .child(toolbar_button("Lock", false)
                        .on_click(cx.listener(|this, _, _, cx| this.lock_selected(cx))))
                    .child(toolbar_button("Instant Platinum", false)
                        .on_click(cx.listener(|this, _, _, cx| this.instant_platinum(cx))))
                    .child(toolbar_button("Clear Unsynced", false)
                        .on_click(cx.listener(|this, _, _, cx| this.clear_unsynced(cx))))
                    .child(toolbar_button("Resign Profile", false)
                        .on_click(cx.listener(|this, _, _, cx| this.select_resign_profile(cx))))
                    .child(toolbar_button("Save", false)
                        .on_click(cx.listener(|this, _, _, cx| this.save(cx))))
                    .child(toolbar_button("Refresh", false)
                        .on_click(cx.listener(|this, _, _, cx| this.refresh_folder(cx))))
                    .child(toolbar_button("Close", false)
                        .on_click(cx.listener(|this, _, _, cx| this.close_folder(cx))))
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
                                            .h(px(50.)).w_full()
                                            .px_3().flex().items_center().gap_2()
                                            .bg(rgb(if selected { 0x29466a } else if index % 2 == 0 { BG } else { 0x151923 }))
                                            .border_b_1().border_color(rgb(0x242a34))
                                            .text_sm()
                                            .cursor_pointer()
                                            .child(div().w(px(48.)).h(px(42.))
                                                .when_some(icon, |d, image|
                                                    d.child(gpui::img(image).w(px(42.)).h(px(42.)))))
                                            .child(div().w(px(45.)).text_color(rgb(MUTED)).child(format!("#{id:03}")))
                                            .child(div().w(px(190.)).overflow_hidden().child(trophy.name))
                                            .child(div().w(px(280.)).overflow_hidden().text_color(rgb(MUTED)).child(trophy.description.clone()))
                                            .child(div().w(px(78.)).text_color(rgb(match trophy.grade { ps3trophyisgood::Grade::Platinum => 0xf4cce2, ps3trophyisgood::Grade::Gold => 0xf9d98a, ps3trophyisgood::Grade::Silver => 0xd1daec, ps3trophyisgood::Grade::Bronze => 0xddac7e })).child(trophy.grade.label()))
                                            .child(div().w(px(58.)).child(if trophy.hidden { "Yes" } else { "No" }))
                                            .child(div().w(px(55.)).child(if trophy.obtained { "Yes" } else { "No" }))
                                            .child(div().w(px(62.)).child(if trophy.synced { "Yes" } else { "No" }))
                                            .child(div().w(px(190.)).text_color(rgb(if trophy.obtained { TEXT } else { MUTED }))
                                                .child(format_timestamp(trophy.earned_us)))
                                            .child(div().w(px(72.)).text_color(rgb(MUTED))
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
        cx.bind_keys(default_bindings().as_keybindings(Some(DEFAULT_INPUT_CONTEXT)));
        let bounds = Bounds::centered(None, size(px(1320.), px(780.)), cx);
        if let Err(error) = cx.open_window(
            WindowOptions {
                window_bounds: Some(WindowBounds::Windowed(bounds)),
                ..Default::default()
            },
            |_, cx| cx.new(|cx| TrophyApp::new(cx)),
        ) {
            eprintln!("Could not create GPU window: {error}");
        }
        cx.activate(true);
    });
}
