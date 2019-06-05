const webpack = require('webpack');

module.exports = {
    entry: ['babel-polyfill', './PersonnelList/personnelList.js'],
    module: {
        rules: [
            {
                test: /\.m?js$/,
                exclude: /(node_modules|bower_components)/,
                use: {
          loader: "babel-loader"
        }
            }
        ]
    },
    resolve: {
        extensions: ['*', '.js', '.jsx']
    },
    output: {
        path: __dirname + '../../../../../wwwroot/js/react',
        publicPath: '/',
        filename: 'personnelList.js'
    }
};
