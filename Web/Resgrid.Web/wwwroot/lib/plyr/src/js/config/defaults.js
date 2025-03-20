// ==========================================================================
// Plyr default config
// ==========================================================================

const defaults = {
    // Disable
    enabled: true,

    // Custom media title
    title: '',

    // Logging to console
    debug: false,

    // Auto play (if supported)
    autoplay: false,

    // Only allow one media playing at once (vimeo only)
    autopause: true,

    // Allow inline playback on iOS (this effects YouTube/Vimeo - HTML5 requires the attribute present)
    // TODO: Remove iosNative fullscreen option in favour of this (logic needs work)
    playsinline: true,

    // Default time to skip when rewind/fast forward
    seekTime: 10,

    // Default volume
    volume: 1,
    muted: false,

    // Pass a custom duration
    duration: null,

    // Display the media duration on load in the current time position
    // If you have opted to display both duration and currentTime, this is ignored
    displayDuration: true,

    // Invert the current time to be a countdown
    invertTime: true,

    // Clicking the currentTime inverts it's value to show time left rather than elapsed
    toggleInvert: true,

    // Aspect ratio (for embeds)
    ratio: '16:9',

    // Click video container to play/pause
    clickToPlay: true,

    // Auto hide the controls
    hideControls: true,

    // Reset to start when playback ended
    resetOnEnd: false,

    // Disable the standard context menu
    disableContextMenu: true,

    // Sprite (for icons)
    loadSprite: true,
    iconPrefix: 'plyr',
    iconUrl: 'https://cdn.plyr.io/3.4.8/plyr.svg',

    // Blank video (used to prevent errors on source change)
    blankVideo: 'https://cdn.plyr.io/static/blank.mp4',

    // Quality default
    quality: {
        default: 576,
        options: [4320, 2880, 2160, 1440, 1080, 720, 576, 480, 360, 240],
    },

    // Set loops
    loop: {
        active: false,
        // start: null,
        // end: null,
    },

    // Speed default and options to display
    speed: {
        selected: 1,
        options: [0.5, 0.75, 1, 1.25, 1.5, 1.75, 2],
    },

    // Keyboard shortcut settings
    keyboard: {
        focused: true,
        global: false,
    },

    // Display tooltips
    tooltips: {
        controls: false,
        seek: true,
    },

    // Captions settings
    captions: {
        active: false,
        language: 'auto',
        // Listen to new tracks added after Plyr is initialized.
        // This is needed for streaming captions, but may result in unselectable options
        update: false,
    },

    // Fullscreen settings
    fullscreen: {
        enabled: true, // Allow fullscreen?
        fallback: true, // Fallback for vintage browsers
        iosNative: false, // Use the native fullscreen in iOS (disables custom controls)
    },

    // Local storage
    storage: {
        enabled: true,
        key: 'plyr',
    },

    // Default controls
    controls: [
        'play-large',
        // 'restart',
        // 'rewind',
        'play',
        // 'fast-forward',
        'progress',
        'current-time',
        'mute',
        'volume',
        'captions',
        'settings',
        'pip',
        'airplay',
        // 'download',
        'fullscreen',
    ],
    settings: ['captions', 'quality', 'speed'],

    // Localisation
    i18n: {
        restart: 'Restart',
        rewind: 'Rewind {seektime}s',
        play: 'Play',
        pause: 'Pause',
        fastForward: 'Forward {seektime}s',
        seek: 'Seek',
        seekLabel: '{currentTime} of {duration}',
        played: 'Played',
        buffered: 'Buffered',
        currentTime: 'Current time',
        duration: 'Duration',
        volume: 'Volume',
        mute: 'Mute',
        unmute: 'Unmute',
        enableCaptions: 'Enable captions',
        disableCaptions: 'Disable captions',
        download: 'Download',
        enterFullscreen: 'Enter fullscreen',
        exitFullscreen: 'Exit fullscreen',
        frameTitle: 'Player for {title}',
        captions: 'Captions',
        settings: 'Settings',
        menuBack: 'Go back to previous menu',
        speed: 'Speed',
        normal: 'Normal',
        quality: 'Quality',
        loop: 'Loop',
        start: 'Start',
        end: 'End',
        all: 'All',
        reset: 'Reset',
        disabled: 'Disabled',
        enabled: 'Enabled',
        advertisement: 'Ad',
        qualityBadge: {
            2160: '4K',
            1440: 'HD',
            1080: 'HD',
            720: 'HD',
            576: 'SD',
            480: 'SD',
        },
    },

    // URLs
    urls: {
        download: null,
        vimeo: {
            sdk: 'https://player.vimeo.com/api/player.js',
            iframe: 'https://player.vimeo.com/video/{0}?{1}',
            api: 'https://vimeo.com/api/v2/video/{0}.json',
        },
        youtube: {
            sdk: 'https://www.youtube.com/iframe_api',
            api:
                'https://www.googleapis.com/youtube/v3/videos?id={0}&key={1}&fields=items(snippet(title))&part=snippet',
        },
        googleIMA: {
            sdk: 'https://imasdk.googleapis.com/js/sdkloader/ima3.js',
        },
    },

    // Custom control listeners
    listeners: {
        seek: null,
        play: null,
        pause: null,
        restart: null,
        rewind: null,
        fastForward: null,
        mute: null,
        volume: null,
        captions: null,
        download: null,
        fullscreen: null,
        pip: null,
        airplay: null,
        speed: null,
        quality: null,
        loop: null,
        language: null,
    },

    // Events to watch and bubble
    events: [
        // Events to watch on HTML5 media elements and bubble
        // https://developer.mozilla.org/en/docs/Web/Guide/Events/Media_events
        'ended',
        'progress',
        'stalled',
        'playing',
        'waiting',
        'canplay',
        'canplaythrough',
        'loadstart',
        'loadeddata',
        'loadedmetadata',
        'timeupdate',
        'volumechange',
        'play',
        'pause',
        'error',
        'seeking',
        'seeked',
        'emptied',
        'ratechange',
        'cuechange',

        // Custom events
        'download',
        'enterfullscreen',
        'exitfullscreen',
        'captionsenabled',
        'captionsdisabled',
        'languagechange',
        'controlshidden',
        'controlsshown',
        'ready',

        // YouTube
        'statechange',

        // Quality
        'qualitychange',

        // Ads
        'adsloaded',
        'adscontentpause',
        'adscontentresume',
        'adstarted',
        'adsmidpoint',
        'adscomplete',
        'adsallcomplete',
        'adsimpression',
        'adsclick',
    ],

    // Selectors
    // Change these to match your template if using custom HTML
    selectors: {
        editable: 'input, textarea, select, [contenteditable]',
        container: '.plyr',
        controls: {
            container: null,
            wrapper: '.plyr__controls',
        },
        labels: '[data-plyr]',
        buttons: {
            play: '[data-plyr="play"]',
            pause: '[data-plyr="pause"]',
            restart: '[data-plyr="restart"]',
            rewind: '[data-plyr="rewind"]',
            fastForward: '[data-plyr="fast-forward"]',
            mute: '[data-plyr="mute"]',
            captions: '[data-plyr="captions"]',
            download: '[data-plyr="download"]',
            fullscreen: '[data-plyr="fullscreen"]',
            pip: '[data-plyr="pip"]',
            airplay: '[data-plyr="airplay"]',
            settings: '[data-plyr="settings"]',
            loop: '[data-plyr="loop"]',
        },
        inputs: {
            seek: '[data-plyr="seek"]',
            volume: '[data-plyr="volume"]',
            speed: '[data-plyr="speed"]',
            language: '[data-plyr="language"]',
            quality: '[data-plyr="quality"]',
        },
        display: {
            currentTime: '.plyr__time--current',
            duration: '.plyr__time--duration',
            buffer: '.plyr__progress__buffer',
            loop: '.plyr__progress__loop', // Used later
            volume: '.plyr__volume--display',
        },
        progress: '.plyr__progress',
        captions: '.plyr__captions',
        caption: '.plyr__caption',
        menu: {
            quality: '.js-plyr__menu__list--quality',
        },
    },

    // Class hooks added to the player in different states
    classNames: {
        type: 'plyr--{0}',
        provider: 'plyr--{0}',
        video: 'plyr__video-wrapper',
        embed: 'plyr__video-embed',
        embedContainer: 'plyr__video-embed__container',
        poster: 'plyr__poster',
        posterEnabled: 'plyr__poster-enabled',
        ads: 'plyr__ads',
        control: 'plyr__control',
        controlPressed: 'plyr__control--pressed',
        playing: 'plyr--playing',
        paused: 'plyr--paused',
        stopped: 'plyr--stopped',
        loading: 'plyr--loading',
        hover: 'plyr--hover',
        tooltip: 'plyr__tooltip',
        cues: 'plyr__cues',
        hidden: 'plyr__sr-only',
        hideControls: 'plyr--hide-controls',
        isIos: 'plyr--is-ios',
        isTouch: 'plyr--is-touch',
        uiSupported: 'plyr--full-ui',
        noTransition: 'plyr--no-transition',
        display: {
            time: 'plyr__time',
        },
        menu: {
            value: 'plyr__menu__value',
            badge: 'plyr__badge',
            open: 'plyr--menu-open',
        },
        captions: {
            enabled: 'plyr--captions-enabled',
            active: 'plyr--captions-active',
        },
        fullscreen: {
            enabled: 'plyr--fullscreen-enabled',
            fallback: 'plyr--fullscreen-fallback',
        },
        pip: {
            supported: 'plyr--pip-supported',
            active: 'plyr--pip-active',
        },
        airplay: {
            supported: 'plyr--airplay-supported',
            active: 'plyr--airplay-active',
        },
        tabFocus: 'plyr__tab-focus',
    },

    // Embed attributes
    attributes: {
        embed: {
            provider: 'data-plyr-provider',
            id: 'data-plyr-embed-id',
        },
    },

    // API keys
    keys: {
        google: null,
    },

    // Advertisements plugin
    // Register for an account here: http://vi.ai/publisher-video-monetization/?aid=plyrio
    ads: {
        enabled: false,
        publisherId: '',
    },
};

export default defaults;
