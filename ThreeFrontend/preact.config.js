module.exports = (config, env, helpers) => {
    const postCssLoaders = helpers.getLoadersByName(config, 'postcss-loader');
    postCssLoaders.forEach(({ loader }) => {
      const plugins = loader.options.postcssOptions.plugins;

      // Add tailwind css at the top.
      plugins.unshift(require('tailwindcss'));
    });
    return config;
  };
