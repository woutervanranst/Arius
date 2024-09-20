const webpack = require('webpack');
const path = require('path');
const fs = require('fs');
const glob = require('glob');
const CopyWebpackPlugin = require('copy-webpack-plugin');
const MiniCssExtractPlugin = require('mini-css-extract-plugin');
const TerserJSPlugin = require('terser-webpack-plugin');
const CssMinimizerPlugin = require('css-minimizer-webpack-plugin');
const WebpackMessages = require('webpack-messages');
const ExcludeAssetsPlugin = require('webpack-exclude-assets-plugin');
const MergeIntoSingle = require('webpack-merge-and-include-globally/index');
const RtlCssPlugin = require('rtlcss-webpack-plugin');

const dev = false;

// paths
let rootPath = path.resolve(__dirname, '..');
const corePath = rootPath;
const coreSrcPath = corePath + '/src';

// arguments/params from the line command
const args = getParameters();

// get theme name
let theme = getTheme(args);

if (args.indexOf('docs_html') !== -1 || args.indexOf('docs_new_html') !== -1) {
    theme = 'docs';
}

if (dev) {
    let f = '';
    if (args.indexOf('docs_new_html') !== -1) {
        f = 'new-';
    }
    rootPath = path.resolve(__dirname, '../../../themes/' + theme + '/' + f + 'html');
}

// get selected demo
let demo = getDemo();

// under demo paths
const demoPath = rootPath + '/src' + (demo ? '/' + demo : '');
const distPath = demoPath;
const assetDistPath = distPath + '/../../wwwroot/assets'
const srcPath = demoPath;

if (dev) {
    console.log(`Source: ${srcPath.replace(/\\/g, '/')}`);
    console.log(`Output: ${assetDistPath.replace(/\\/g, '/')}`);
}

const extraPlugins = [];
const exclude = [];

const js = args.indexOf('js') !== -1;
const css = args.indexOf('css') !== -1 || args.indexOf('scss') !== -1;

additionalSettings();

importExtraPlugins();

function additionalSettings() {
    if (args.indexOf('rtl') !== -1) {
        // enable rtl for css
        extraPlugins.push(new RtlCssPlugin({
            filename: '[name].rtl.css',
        }));
    }

    if (!js && css) {
        // exclude js files
        exclude.push('\.js$');
    }

    if (js && !css) {
        // exclude css files
        exclude.push('\.s?css$');
    }

    if (exclude.length) {
        // add plugin for exclude assets (js/css)
        extraPlugins.push(new ExcludeAssetsPlugin({
            path: exclude,
        }));
    }
}

function getEntryFiles() {

    const entries = {
        // 3rd party plugins css/js
        'plugins/global/plugins.bundle': ['./webpack/plugins/plugins.js', './webpack/plugins/plugins.scss'],
        // Theme css/js
        'css/style.bundle': ['./' + path.relative('./', srcPath) + '/sass/style.scss', './' + path.relative('./', srcPath) + '/sass/plugins.scss'],
        'js/scripts.bundle': './webpack/scripts' + (demo ? '.' + demo : '') + '.js',
    };

    // Custom 3rd party plugins
    (glob.sync('./webpack/{plugins,js}/custom/**/*.+(js)') || []).forEach(file => {
        let loc = file.replace('webpack/', '').replace('./', '');
        loc = loc.replace('.js', '.bundle');
        entries[loc] = './' + file;
    });

    // Custom JS files from src folder
    (glob.sync(path.relative('./', srcPath) + '/js/custom/**/!(_)*.js') || [])
        .filter(f => {
            // exclude folder with bundle
            return /\/bundle\/.*?\.js/.test(f) === false;
        })
        .forEach(file => {
            entries[file.replace(/.*js\/(.*?)\.js$/ig, 'js/$1')] = './' + file;
        });

    entries['js/widgets.bundle'] = (glob.sync(path.relative('./', srcPath).replaceAll(/\\/g, '/') + '/js/widgets/**/!(_)*.js') || []).map(file => {
        return file.replaceAll(/\\/g, '/');
    });

    if (dev) {
        entries['js/scripts.bundle'] = './' + path.relative('./', rootPath) + '/tools/webpack/scripts' + (demo ? '.' + demo : '') + '.js';

        // Custom 3rd party plugins from theme folder
        (glob.sync('./' + path.relative('./', rootPath) + '/tools/webpack/{plugins,js}/custom/**/*.+(js)') || []).forEach(file => {
            let loc = file.replace(/^.*?webpack/g, '').replace('./', '');
            loc = loc.replace('.js', '.bundle');
            entries[loc] = './' + file;
        });

        // Custom JS files from core src folder
        (glob.sync(path.relative('./', coreSrcPath) + '/js/custom/**/!(_)*.js') || []).forEach(file => {
            entries[file.replace(/.*js\/(.*?)\.js$/ig, 'js/$1')] = './' + file;
        });

        // Widgets js
        entries['js/widgets.bundle'] = (glob.sync(path.relative('./', coreSrcPath) + '/js/widgets/**/!(_)*.js') || []);
    }

    if (args.indexOf('docs_html') !== -1 || args.indexOf('docs_new_html') !== -1) {
        entries['js/scripts.bundle'] = './' + path.relative('./', rootPath) + '/src/js/scripts.js';
    }

    return entries;
}

function mainConfig() {
    return {
        // enabled/disable optimizations
        mode: args.indexOf('production') !== -1 ? 'production' : 'development',
        // console logs output, https://webpack.js.org/configuration/stats/
        stats: 'errors-warnings',
        /*ignoreWarnings: [{
            module: /esri-leaflet/,
            message: /version/,
        }],*/
        performance: {
            // disable warnings hint
            hints: false,
        },
        optimization: {
            minimize: args.indexOf('production') !== -1,
            // js and css minimizer
            minimizer: [new TerserJSPlugin(), new CssMinimizerPlugin()],
        },
        entry: getEntryFiles(),
        output: {
            // main output path in assets folder
            path: assetDistPath,
            // output path based on the entries' filename
            filename: '[name].js',
        },
        resolve: {
            alias: {
'@/src': [demoPath],
                jquery: path.join(__dirname, 'node_modules/jquery/src/jquery'),
                $: path.join(__dirname, 'node_modules/jquery/src/jquery'),
                '@': [demoPath, corePath],
                'handlebars': 'handlebars/dist/handlebars.js',
                '@form-validation': (dev ? coreSrcPath : srcPath) + '/plugins/@form-validation/cjs',
            },
            extensions: ['.js', '.scss'],
            fallback: {
                util: false,
            },
        },
        // devtool: 'source-map',
        plugins: [
            new WebpackMessages({
                name: theme,
                logger: str => console.log(`>> ${str}`),
            }),
            // create css file
            new MiniCssExtractPlugin({
                filename: '[name].css',
            }),
            new CopyWebpackPlugin({
                patterns: copyFolders(),
            }),
        ].concat(extraPlugins),
        module: {
            rules: [
                {
                    test: /\.css$/,
                    use: [
                        MiniCssExtractPlugin.loader,
                        'css-loader',
                    ],
                },
                {
                    test: /\.scss$/,
                    use: [
                        MiniCssExtractPlugin.loader,
                        {
                            loader: 'css-loader',
                        },
                        {
                            loader: 'sass-loader',
                            options: {
                                // Prefer `dart-sass`
                                implementation: require("sass"),
                                sourceMap: false,
                                sassOptions: {
                                    includePaths: [
                                        demoPath,
                                        path.resolve(__dirname, 'node_modules'),
                                    ],
                                },
                            },
                        },
                    ],
                },
                {
                    test: /\.(ttf|otf|eot|svg|woff(2)?)(\?[a-z0-9]+)?$/,
                    type: 'asset/resource',
                    generator: {
                      filename: './plugins/global/fonts/[name][ext]'
                    }
                },
                {
                    test: /\.(gif|png|jpe?g)$/,
                    type: 'asset/resource',
                    generator: {
                      filename: './plugins/custom/images/[name][ext]'
                    },
                },
                // {
                //     // for demo8 image in scss
                //     test: /\.(gif|png|jpe?g)$/,
                //     use: [
                //         {
                //             loader: 'url-loader',
                //             options: {
                //                 emitFile: false,
                //                 name: '[path][name].[ext]',
                //                 publicPath: (url, resourcePath, context) => {
                //                     return '../';
                //                 },
                //             },
                //         },
                //     ],
                // },
            ],
        },
        // webpack dev server config
        devServer: {
            static: {
              directory: distPath,
            },
            compress: true,
            port: 8080,
        },
    };
}

function copyFolders() {
    let options = [
        {
            // copy media
            from: srcPath + '/media',
            to: assetDistPath + '/media',
        },
        {
            // copy tinymce skins
            from: path.resolve(__dirname, 'node_modules') + '/tinymce/skins',
            to: assetDistPath + '/plugins/custom/tinymce/skins',
        },
        {
            // copy tinymce plugins
            from: path.resolve(__dirname, 'node_modules') + '/tinymce/plugins',
            to: assetDistPath + '/plugins/custom/tinymce/plugins',
        }
    ];

    if (fs.existsSync(coreSrcPath + '/media/plugins/jstree')) {
        options.push({
            // copy jstree image
            from: coreSrcPath + '/media/plugins/jstree',
            to: assetDistPath + '/plugins/custom/jstree',
            force: true
        });
    }

    if (dev) {
        options.push({
            // copy media from core
            from: coreSrcPath + '/media',
            to: assetDistPath + '/media',
        });
    }

    return options;
}

function getParameters() {
    var args = [];
    Object.keys(process.env).forEach(function (key) {
        if (key.startsWith('npm_config_')) {
            var arg = key.substring('npm_config_'.length);
            args.push(arg);
        }
    });

    if ('production' === process.env['NODE_ENV']) {
        args.push('production');
    }

    return args;
}

function importExtraPlugins() {
    // Optional: Import datatables.net
    extraPlugins.push(new MergeIntoSingle({
        files: {
            'plugins/custom/datatables/datatables.bundle.js': [
                "node_modules/datatables.net/js/dataTables.js",
                "node_modules/datatables.net-bs5/js/dataTables.bootstrap5.js",
                "../src/js/vendors/plugins/datatables.init.js",
                "node_modules/jszip/dist/jszip.min.js",
                "node_modules/pdfmake/build/pdfmake.min.js",
                "node_modules/pdfmake/build/vfs_fonts.js",
                "node_modules/datatables.net-buttons/js/dataTables.buttons.min.js",
                "node_modules/datatables.net-buttons-bs5/js/buttons.bootstrap5.min.js",
                "node_modules/datatables.net-buttons/js/buttons.colVis.js",
                "node_modules/datatables.net-buttons/js/buttons.flash.js",
                "node_modules/datatables.net-buttons/js/buttons.html5.js",
                "node_modules/datatables.net-buttons/js/buttons.print.js",
                "node_modules/datatables.net-colreorder/js/dataTables.colReorder.min.js",
                "node_modules/datatables.net-colreorder-bs5/js/colReorder.bootstrap5.js",
                "node_modules/datatables.net-fixedcolumns/js/dataTables.fixedColumns.min.js",
                "node_modules/datatables.net-fixedcolumns-bs5/js/fixedColumns.bootstrap5.js",
                "node_modules/datatables.net-fixedheader/js/dataTables.fixedHeader.min.js",
                "node_modules/datatables.net-fixedheader-bs5/js/fixedHeader.bootstrap5.js",
                "node_modules/datatables.net-responsive/js/dataTables.responsive.min.js",
                "node_modules/datatables.net-responsive-bs5/js/responsive.bootstrap5.min.js",
                "node_modules/datatables.net-rowgroup/js/dataTables.rowGroup.min.js",
                "node_modules/datatables.net-rowgroup-bs5/js/rowGroup.bootstrap5.js",
                "node_modules/datatables.net-rowreorder/js/dataTables.rowReorder.min.js",
                "node_modules/datatables.net-rowreorder-bs5/js/rowReorder.bootstrap5.js",
                "node_modules/datatables.net-scroller/js/dataTables.scroller.min.js",
                "node_modules/datatables.net-scroller-bs5/js/dataTables.bootstrap5.js",
                "node_modules/datatables.net-select/js/dataTables.select.min.js",
                "node_modules/datatables.net-select-bs5/js/dataTables.bootstrap5.js",
                "node_modules/datatables.net-datetime/dist/dataTables.dateTime.min.js",
            ],
            'plugins/custom/datatables/pdfmake.min.js.map': [
                'node_modules/pdfmake/build/pdfmake.min.js.map',
            ],
            'plugins/custom/datatables/datatables.bundle.css': [
                "node_modules/datatables.net-bs5/css/dataTables.bootstrap5.css",
                "node_modules/datatables.net-buttons-bs5/css/buttons.bootstrap5.min.css",
                "node_modules/datatables.net-colreorder-bs5/css/colReorder.bootstrap5.min.css",
                "node_modules/datatables.net-fixedcolumns-bs5/css/fixedColumns.bootstrap5.min.css",
                "node_modules/datatables.net-fixedheader-bs5/css/fixedHeader.bootstrap5.min.css",
                "node_modules/datatables.net-responsive-bs5/css/responsive.bootstrap5.min.css",
                "node_modules/datatables.net-rowreorder-bs5/css/rowReorder.bootstrap5.min.css",
                "node_modules/datatables.net-scroller-bs5/css/scroller.bootstrap5.min.css",
                "node_modules/datatables.net-select-bs5/css/select.bootstrap5.min.css",
                "node_modules/datatables.net-datetime/dist/dataTables.dateTime.min.css",
            ],

            // fullcalendar
            'plugins/custom/fullcalendar/fullcalendar.bundle.js': [
                'node_modules/fullcalendar/main.js',
                'node_modules/fullcalendar/locales-all.min.js',
            ],
            'plugins/custom/fullcalendar/fullcalendar.bundle.css': [
                'node_modules/fullcalendar/main.min.css',
            ],
        },
    }));
}

function getTheme() {

    const excludedKeys = [
        'npm_config_cache',
        'npm_config_globalconfig',
        'npm_config_global_prefix',
        'npm_config_init_module',
        'npm_config_local_prefix',
        'npm_config_metrics_registry',
        'npm_config_node_gyp',
        'npm_config_noproxy',
        'npm_config_prefix',
        'npm_config_userconfig',
        'npm_config_user_agent',
        'npm_config_npm_version',
    ];

    const key = Object.keys(process.env)
        .filter(element => !element.match(/npm_config_(demo\d+)$/))
        .filter(key => !excludedKeys.includes(key))
        .find(element => element.match(/npm_config_.*?/));

    if (key) {
        return key.replace('npm_config_', '');
    }

    return null;
}

function getDemo() {
    const key = Object.keys(process.env).find(element => element.match(/npm_config_(demo\d+)$/));

    let demo = null;

    if (key) {
        demo = key.replace('npm_config_', '');
    }

    return demo;
}

module.exports = () => {
    return [mainConfig()];
};
